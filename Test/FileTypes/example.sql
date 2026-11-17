-- A small SQL script: a table of files, some rows and a report.
CREATE TABLE files (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    size_kb REAL DEFAULT 0
);

INSERT INTO files (name, size_kb) VALUES ('example', 1.5), ('notes', 32.25), ('empty', 0);

/* A block comment
   over two lines. */
SELECT name, size_kb
FROM files
WHERE size_kb > 0 AND name LIKE 'e%'
ORDER BY size_kb DESC
LIMIT 10;

SELECT COUNT(*) AS total, 'Hello from iTerminal' AS greeting
FROM files
GROUP BY size_kb
HAVING COUNT(*) >= 1;
