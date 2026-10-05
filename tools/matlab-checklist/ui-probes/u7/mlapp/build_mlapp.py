"""Builds the .mlapp files the U7 parity fixtures run, from plain class text.

An .mlapp is an OPC zip whose matlab/document.xml holds the whole classdef in one CDATA run
(research C, section 1). The files made here hold the three parts R2025b needs to run an app and
nothing else - no appModel.mat - so they are apps App Designer could not open and MATLAB runs.
The zips are written with a fixed date so that a rebuild gives the same bytes.

    python build_mlapp.py        (writes into tests/JGraph.Tests/MatlabParity/fixtures/helpers)
"""
import os
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", "..", ".."))
HELPERS = os.path.join(REPO, "tests", "JGraph.Tests", "MatlabParity", "fixtures", "helpers")

CT = """<?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default ContentType="application/vnd.openxmlformats-package.relationships+xml" Extension="rels"/>
  <Default ContentType="application/vnd.mathworks.matlab.code.document+xml;plaincode=true" Extension="xml"/>
</Types>"""


def rels(target):
    return ("""<?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Target="%s" Type="http://schemas.mathworks.com/matlab/code/2013/relationships/document"/>
</Relationships>""" % target)


def doc(code):
    return ('<?xml version="1.0" encoding="UTF-8" standalone="no" ?><w:document '
            'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p>'
            '<w:pPr><w:pStyle w:val="code"/></w:pPr><w:r><w:t><![CDATA[' + code + ']]></w:t></w:r></w:p></w:body></w:document>')


def write(name, parts):
    path = os.path.join(HELPERS, name)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for part, text in parts:
            info = zipfile.ZipInfo(part, date_time=(2026, 10, 4, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, text.encode("utf-8"))
    print(name)


def opc(code, part="matlab/document.xml"):
    return [("[Content_Types].xml", CT), ("_rels/.rels", rels(part)), (part, doc(code))]


def source(name):
    with open(os.path.join(HERE, name), encoding="utf-8", newline="") as f:
        return f.read().replace("\r\n", "\n")


# The typical app, from the exported .m the fixtures also run: MATLAB's "Export to .m" changes the
# class name and nothing else, so the .mlapp is that text with the name changed back.
with open(os.path.join(HELPERS, "U7App.m"), encoding="utf-8", newline="") as f:
    app = f.read().replace("\r\n", "\n")
write("U7MlApp.mlapp", opc(app.replace("U7App", "U7MlApp").replace("U7 App", "U7 MlApp")))

# One name as an .mlapp and as an .m in one folder: the .mlapp is the one that runs.
write("U7Same.mlapp", opc(source("U7Same.mlapp.txt")))

# An .mlapp need not be an app: a plain handle class, and its document under another part name.
write("U7InZip.mlapp", opc(source("U7InZip.txt"), part="code/main.xml"))

# No OPC parts at all, only the document where it usually is.
write("U7Bare.mlapp", [("matlab/document.xml", doc(source("U7InZip.txt").replace("U7InZip", "U7Bare")))])

# A function instead of a class, and a class under the wrong name.
write("U7Fn.mlapp", opc(source("U7Fn.txt")))
write("U7Wrong.mlapp", opc(source("U7InZip.txt").replace("U7InZip", "U7Other")))

# A class whose method fails, for the line an error is reported at.
write("U7Throws.mlapp", opc(source("U7Throws.txt")))
