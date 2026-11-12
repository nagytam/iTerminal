using System.Data;
using System.Runtime.InteropServices;
using iTerminal.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace iTerminal.Core.Services.Documents;

/// <summary>
/// The meaning index: one vector per passage of every embedded document, in two raw SQL tables.
///
/// <para>Like the FTS table (<see cref="DocumentSearchIndex"/>) these tables are invisible to EF Core. A
/// model change would make the application drop every cached table, and the vectors are derived data that
/// can be rebuilt from <c>DocumentContents</c>, so they are created on demand and every method runs
/// <c>CREATE TABLE IF NOT EXISTS</c> first: a database that was recreated at start-up gets them back
/// without a flag that could go stale.</para>
///
/// <para><c>DocumentEmbeddingState</c> is the record of which model a document was embedded with. A document
/// is current when its state row names the configured model; anything else (no row, or another model) is
/// pending. The vectors are never trusted without it, so a model change needs no sweep of the vector table.</para>
///
/// <para>Search is brute force: every vector of the current model is compared with the query. That is
/// exact and needs no index library, and it is fast for a personal library. The vectors are float32 and held in
/// memory once a search has loaded them, so a library of 10,000 documents at about four passages each and 768
/// dimensions costs roughly 120 MB while meaning search is in use. That is the price of this design.</para>
/// </summary>
public sealed class DocumentVectorIndex
{
    private const string StateTable = "DocumentEmbeddingState";
    private const string VectorTable = "DocumentEmbeddings";

    /// <summary>The best-scoring passage of one document for a query.</summary>
    /// <param name="Path">The document.</param>
    /// <param name="ChunkStart">Where its best passage starts in the extracted text.</param>
    /// <param name="Score">Cosine similarity, from -1 to 1; larger is closer in meaning.</param>
    public readonly record struct Match(string Path, int ChunkStart, double Score);

    /// <summary>One passage's vector, ready to compare.</summary>
    private sealed record Passage(string Path, int ChunkStart, float[] Vector, float Norm);

    private sealed record Snapshot(string Model, IReadOnlyList<Passage> Passages);

    private readonly Lock _lock = new();

    // Bumped by every write. A search that loaded its snapshot before a write must not store it afterwards.
    private long _version;
    private Snapshot? _snapshot;

    /// <summary>Documents embedded with <paramref name="model"/>.</summary>
    public int Count(iTerminalDbContext context, string model)
    {
        EnsureCreated(context);
        using SqliteCommand command = CreateCommand(context, $"SELECT COUNT(*) FROM {StateTable} WHERE Model = $model;");
        command.Parameters.AddWithValue("$model", model);
        object? result = command.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    /// <summary>
    /// The documents whose text is in <c>DocumentContents</c> but whose vectors are missing or were made by another
    /// model. Sorted, so a run is repeatable.
    /// </summary>
    public List<string> Pending(iTerminalDbContext context, string model)
    {
        EnsureCreated(context);
        using SqliteCommand command = CreateCommand(
            context,
            // A text row with no metadata row is a leftover of an interrupted delete: it is not listed, so it is not embedded
            // either, and it does not sit in the pending list forever.
            "SELECT c.Path FROM DocumentContents c "
            + "JOIN Documents d ON d.Path = c.Path "
            + $"LEFT JOIN {StateTable} s ON s.Path = c.Path "
            + "WHERE s.Path IS NULL OR s.Model <> $model ORDER BY c.Path;");
        command.Parameters.AddWithValue("$model", model);

        List<string> paths = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }

        return paths;
    }

    /// <summary>
    /// Replaces a document's vectors and records the model they came from. Call inside the transaction that
    /// wrote the text the vectors describe, so the two cannot disagree.
    /// </summary>
    public void Replace(iTerminalDbContext context, string path, string model, IReadOnlyList<(int ChunkStart, string Passage, float[] Vector)> passages)
    {
        EnsureCreated(context);
        DeleteFor(context, path);

        using (SqliteCommand state = CreateCommand(context, $"INSERT INTO {StateTable}(Path, Model) VALUES ($path, $model);"))
        {
            state.Parameters.AddWithValue("$path", path);
            state.Parameters.AddWithValue("$model", model);
            state.ExecuteNonQuery();
        }

        using SqliteCommand insert = CreateCommand(
            context,
            $"INSERT INTO {VectorTable}(Path, ChunkStart, Passage, Vector) VALUES ($path, $start, $passage, $vector);");
        SqliteParameter path0 = insert.Parameters.Add("$path", SqliteType.Text);
        SqliteParameter start = insert.Parameters.Add("$start", SqliteType.Integer);
        SqliteParameter passage = insert.Parameters.Add("$passage", SqliteType.Text);
        SqliteParameter vector = insert.Parameters.Add("$vector", SqliteType.Blob);

        foreach ((int chunkStart, string text, float[] values) in passages)
        {
            path0.Value = path;
            start.Value = chunkStart;
            passage.Value = text;
            vector.Value = ToBytes(values);
            insert.ExecuteNonQuery();
        }

        Invalidate();
    }

    /// <summary>Forgets the vectors and the model record of each document, for a file that changed or went away.</summary>
    public void Remove(iTerminalDbContext context, IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        EnsureCreated(context);
        foreach (string path in paths)
        {
            DeleteFor(context, path);
        }

        Invalidate();
    }

    /// <summary>
    /// The text of one stored passage, or empty when there is none. The passage is stored as it was embedded, so a results
    /// row needs no offset into the document's text (SQL substring counts characters, not the UTF-16 units the offsets use).
    /// </summary>
    public string ReadPassage(iTerminalDbContext context, string path, int chunkStart)
    {
        // The tables are made by the Search that produced these offsets; not repeated per row, which would be 50 DDL statements per query.
        using SqliteCommand command = CreateCommand(
            context,
            $"SELECT Passage FROM {VectorTable} WHERE Path = $path AND ChunkStart = $start;");
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$start", chunkStart);
        return command.ExecuteScalar() as string ?? string.Empty;
    }

    /// <summary>
    /// The best passage of each document, most similar first, at most <paramref name="limit"/> documents. Only
    /// documents embedded with <paramref name="model"/> are considered, and only passages whose vector length matches
    /// the query's.
    /// </summary>
    public IReadOnlyList<Match> Search(iTerminalDbContext context, string model, float[] query, int limit)
    {
        if (query.Length == 0 || limit <= 0)
        {
            return [];
        }

        IReadOnlyList<Passage> passages = Load(context, model);
        float queryNorm = Norm(query);
        if (queryNorm == 0)
        {
            return [];
        }

        Dictionary<string, Match> best = new(StringComparer.Ordinal);
        foreach (Passage passage in passages)
        {
            if (passage.Vector.Length != query.Length || passage.Norm == 0)
            {
                continue;
            }

            double score = Dot(passage.Vector, query) / (passage.Norm * queryNorm);
            if (!best.TryGetValue(passage.Path, out Match current) || score > current.Score)
            {
                best[passage.Path] = new Match(passage.Path, passage.ChunkStart, score);
            }
        }

        return [.. best.Values.OrderByDescending(match => match.Score).Take(limit)];
    }

    private IReadOnlyList<Passage> Load(iTerminalDbContext context, string model)
    {
        lock (_lock)
        {
            if (_snapshot is not null && _snapshot.Model == model)
            {
                return _snapshot.Passages;
            }
        }

        long version;
        lock (_lock)
        {
            version = _version;
        }

        EnsureCreated(context);
        List<Passage> passages = [];
        using (SqliteCommand command = CreateCommand(
                   context,
                   $"SELECT e.Path, e.ChunkStart, e.Vector FROM {VectorTable} e "
                   + $"JOIN {StateTable} s ON s.Path = e.Path WHERE s.Model = $model;"))
        {
            command.Parameters.AddWithValue("$model", model);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                byte[] bytes = (byte[])reader[2];
                if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
                {
                    continue;
                }

                float[] vector = FromBytes(bytes);
                passages.Add(new Passage(reader.GetString(0), reader.GetInt32(1), vector, Norm(vector)));
            }
        }

        lock (_lock)
        {
            // Stored only if nothing was written meanwhile; otherwise the next search reloads.
            if (version == _version)
            {
                _snapshot = new Snapshot(model, passages);
            }
        }

        return passages;
    }

    /// <summary>
    /// Drops the cached snapshot. Writes already do this, but inside a transaction a search can load the rows before the
    /// commit and cache them after the write's own invalidation, so the caller invalidates again once the commit is done.
    /// </summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _version++;
            _snapshot = null;
        }
    }

    private static void DeleteFor(iTerminalDbContext context, string path)
    {
        using (SqliteCommand vectors = CreateCommand(context, $"DELETE FROM {VectorTable} WHERE Path = $path;"))
        {
            vectors.Parameters.AddWithValue("$path", path);
            vectors.ExecuteNonQuery();
        }

        using SqliteCommand state = CreateCommand(context, $"DELETE FROM {StateTable} WHERE Path = $path;");
        state.Parameters.AddWithValue("$path", path);
        state.ExecuteNonQuery();
    }

    private static void EnsureCreated(iTerminalDbContext context)
    {
        using SqliteCommand state = CreateCommand(
            context,
            $"CREATE TABLE IF NOT EXISTS {StateTable}(Path TEXT NOT NULL PRIMARY KEY, Model TEXT NOT NULL);");
        state.ExecuteNonQuery();

        using SqliteCommand vectors = CreateCommand(
            context,
            $"CREATE TABLE IF NOT EXISTS {VectorTable}(Path TEXT NOT NULL, ChunkStart INTEGER NOT NULL, Passage TEXT NOT NULL, Vector BLOB NOT NULL, PRIMARY KEY(Path, ChunkStart));");
        vectors.ExecuteNonQuery();
    }

    /// <summary>The vector as float32 values, in the machine's byte order. The table is local to this machine.</summary>
    internal static byte[] ToBytes(float[] vector) => MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

    internal static float[] FromBytes(byte[] bytes) => MemoryMarshal.Cast<byte, float>(bytes).ToArray();

    private static float Norm(float[] vector) => (float)Math.Sqrt(Dot(vector, vector));

    private static double Dot(float[] left, float[] right)
    {
        double sum = 0;
        for (int index = 0; index < left.Length; index++)
        {
            sum += (double)left[index] * right[index];
        }

        return sum;
    }

    private static SqliteCommand CreateCommand(iTerminalDbContext context, string sql)
    {
        System.Data.Common.DbConnection connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        SqliteCommand command = (SqliteCommand)connection.CreateCommand();
        command.CommandText = sql;

        if (context.Database.CurrentTransaction?.GetDbTransaction() is SqliteTransaction transaction)
        {
            command.Transaction = transaction;
        }

        return command;
    }
}
