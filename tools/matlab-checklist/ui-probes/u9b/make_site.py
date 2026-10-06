"""Builds site/types for probe u9b_types: one small file per extension, plus a few odd names."""
import pathlib

HERE = pathlib.Path(__file__).resolve().parent
TYPES = HERE / "site" / "types"
EXTS = """txt js mjs cjs json css html htm xhtml xml svg png jpg jpeg jfif gif bmp ico webp avif apng tif tiff
woff woff2 ttf otf eot mp3 wav ogg oga opus flac aac m4a mp4 m4v webm ogv mov avi mkv weba vtt srt pdf csv tsv md
m mat mlx mlapp fig wasm map zip gz tar 7z bin dat exe dll bat ps1 py c h cpp java sh yaml yml toml ini log rtf
doc docx xls xlsx ppt pptx ts tsx jsx glb gltf obj stl manifest webmanifest appcache cur jsonld geojson
topojson hdr exr ktx ktx2 basis dds psd ai eps swf jar wmv mid midi""".split()
ODD = ["noext", "a.PNG", "a.JS", "a.Json", "a.b.js", "space name.js", ".hidden", "UPPER.CSS", "a.html.txt", "a.txt.js"]
TYPES.mkdir(parents=True, exist_ok=True)
(TYPES / "sub").mkdir(exist_ok=True)
for ext in EXTS:
    (TYPES / f"a.{ext}").write_bytes(f"content {ext}\n".encode())
for name in ODD:
    (TYPES / name).write_bytes(f"content {name}\n".encode())
(TYPES / "sub" / "deep.js").write_bytes(b"content deep\n")
(TYPES / "sub" / "index.html").write_bytes(b"<p>sub index</p>\n")
(TYPES / "index.html").write_bytes(b"<p>types index</p>\n")
(HERE / "site" / "page.html").write_bytes(b"<!DOCTYPE html><html><body><p>page</p></body></html>\n")
(HERE / "outside.js").write_bytes(b"content outside\n")
print(len(EXTS), "extensions,", len(ODD), "odd names")
