# File type examples

One small example file for each format iTerminal reads, named `example.<extension>` (compound archive endings keep
their full name, such as `example.tar.gz`). The original files in this folder (`iTerminal.zip`, `PDF-Test.pdf`,
`rufus-4.6p.exe`, `test.sh`, `test.cmd`, `test.ps1`) are used by the tests and are kept as they are.

What is here:

- **Archives:** zip, 7z, rar, iso, tar and its compressed forms (gz, tgz, bz2, tbz2, tbz, xz, txz), and single-file
  streams (gz, bz2, xz, lzma). The archives contain `example.txt`, `example.csv` and `example.png`.
- **Images:** png, jpg, jpeg, gif, bmp, webp, tif, tiff (each shows the text "iTerminal example"), svg, ico, cur, heic
  and heif (a 64x64 picture of the same image), and dng (a TIFF with the DNG tags and a 16-bit RGGB mosaic).
- **Video:** mp4, m4v, mkv, avi, mov, webm, wmv, flv, mpg, mpeg, ogv, 3gp, mts, m2ts. Two seconds each, 320x240.
- **Audio:** wav, mp3, flac, ogg, opus, m4a, aac, wma, aiff and the other containers and telephony formats listed in
  `AudioDecoderFactory`, each a two-second 440 Hz tone. Written by ffmpeg: gsm, g723_1, aptxhd, thd, truehd, mlp and
  dts (5.1). Also dsf and dff (1-bit DSD, one second), qoa and vag (both decoded by ffmpeg back to the samples the
  encoder produced).
- **Tracker modules:** mod (ProTracker), m15 and stk (15-sample files, the same bytes), nst (a 31-sample file with the
  NoiseTracker `N.T.` signature), s3m (Scream Tracker 3), xm (FastTracker 2), it and mptm (Impulse Tracker; an MPTM is
  OpenMPT's IT-based format, so the mptm file is an IT file under that name), and stm (Scream Tracker 2). Each was
  loaded by libopenmpt, which named the format and played a note.
- **Documents:** pdf, docx, docm, dotx, dotm, xlsx, xlsm, xltx, xltm, pptx, pptm, potx, potm, ppsx, ppsm, odt, ott, ods,
  ots, odp, otp, odg, otg, and the older binary formats doc, xls, ppt (converted by LibreOffice), rtf, xps, oxps and epub.
- **E-mail and e-books:** eml, msg (an Outlook message in an OLE compound file, with subject, body, sender and
  recipient), fb2, epub, mobi and azw (an uncompressed MOBI book, the same bytes under both names).
- **Databases:** sqlite, sqlite3, db and db3 (a SQLite file with one table of two rows).
- **Network logs:** har (an HTTP Archive 1.2 export: a page, a JSON API call with a query string, a failed login POST, a base64 PNG, a 404 and a stylesheet; F3 shows it as a list of requests).
- **Columnar data:** parquet (an Apache Parquet file written by pyarrow: five rows, with an integer, a string with a null, a float with a null, a boolean, a date and a list column; F3 shows it as a grid of its first rows).
- **Scientific data:** h5, hdf5, hdf, he5 (the same HDF5 file under each extension: an HDF5 file with a 3 x 4 x 5 cube whose cells are `100 i + 10 j + k`, an integer vector, two strings and a matrix in a group; F3 shows it as a grid).
- **Binaries:** dll and exe (a small .NET library and console program), and so (a small ELF shared library).
- **Text, code and markup:** the plain text and source formats the text extractor reads (txt, md, csv, json, yaml,
  srt, vtt, the programming languages listed in `PlainTextExtractor`, and so on), and the markup formats (html, xml,
  xaml, rss, atom, csproj, slnx, config, xsd, xsl, xslt, props, targets, nuspec, resx, plist, sln).
- **Playlists:** m3u, m3u8.
- **Checksums and signatures:** `example.zip.md5`, `.sha1`, `.sha256`, and detached signatures of `example.txt`
  (`.asc`, `.sig`, `.gpg`, `.sign`). The signing key was temporary, so the signatures cannot be verified by a keyring.

Not included (the viewer lists these, but no example could be produced without faking the file):

- **Tracker modules** the libopenmpt decoder reads but this folder has no writer for: mtm (libopenmpt names the format,
  but the sample plays silent), 669, amf, ams, dbm, digi, dmf, dsm, dtm, far, gdm, imf, j2b, mdl, med, mms, mt2, plm,
  psm, ptm, sfx, sfx2, st26, stp, ult, umx, wow, and the compressed wrappers mmcmp, mo3 and xpk. Oktalyzer (okt) was
  written and named by libopenmpt, but it plays silent, so it was left out. pt36 has the same bytes as mod.
- **Decoder-only audio and game formats** (no encoder or spec-level writer here): ape, mpc, ac4, dtshd, tak, oma, omg,
  aa, aa3, aax, acm, aea, afc, apc, avr, mca, dss, lc3, msbc, tco, rco, g728, g729, ivr, laf, wa, genh, brstm, bfstm,
  bcstm, hca, binka, bonk, xvag, wsd, xmd, vqf, vql, vqe, rka, mtaf.
- **msg:** written (see above). **cab:** no CAB writer was available.
