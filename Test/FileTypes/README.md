# File type examples

One small example file for each format iTerminal reads, named `example.<extension>` (compound archive endings keep
their full name, such as `example.tar.gz`). The original files in this folder (`iTerminal.zip`, `PDF-Test.pdf`,
`rufus-4.6p.exe`, `test.sh`, `test.cmd`, `test.ps1`) are used by the tests and are kept as they are.

What is here:

- **Archives:** zip, 7z, rar, iso, tar and its compressed forms (gz, tgz, bz2, tbz2, tbz, xz, txz), and single-file
  streams (gz, bz2, xz, lzma). The archives contain `example.txt`, `example.csv` and `example.png`.
- **Images:** png, jpg, jpeg, gif, bmp, webp, tif, tiff (each shows the text "iTerminal example"), and svg.
- **Video:** mp4, m4v, mkv, avi, mov, webm, wmv, flv, mpg, mpeg, ogv, 3gp, mts, m2ts. Two seconds each, 320x240.
- **Audio:** wav, mp3, flac, ogg, opus, m4a, aac, wma, aiff and the other containers and telephony formats listed in
  `AudioDecoderFactory`, each a two-second 440 Hz tone.
- **Documents:** pdf, docx, docm, xlsx, xlsm, pptx, pptm, odt, ods, odp.
- **Text, code and markup:** the plain text and source formats the text extractor reads (txt, md, csv, json, yaml,
  srt, vtt, the programming languages listed in `PlainTextExtractor`, and so on), and the markup formats (html, xml,
  xaml, rss, atom, csproj, slnx, config).
- **Playlists:** m3u, m3u8.
- **Checksums and signatures:** `example.zip.md5`, `.sha1`, `.sha256`, and detached signatures of `example.txt`
  (`.asc`, `.sig`, `.gpg`, `.sign`). The signing key was temporary, so the signatures cannot be verified by a keyring.

Not included:

- **Tracker modules** (mod, s3m, xm, it and the others) and the decoder-only game and telephony formats (brstm, hca,
  dsf, ape, mpc and similar): no encoder exists on the machine that made these files, so a sample could not be written
  without faking it.
- **cab:** no CAB writer was available.
