"""Build minimal .mlapp packages (no appModel.mat, no metadata) from plain classdef text."""
import os, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "build", "ProbeApp.m.txt")

CT = """<?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default ContentType="application/vnd.openxmlformats-package.relationships+xml" Extension="rels"/>
  <Default ContentType="application/vnd.mathworks.matlab.code.document+xml;plaincode=true" Extension="xml"/>
</Types>"""

RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Target="matlab/document.xml" Type="http://schemas.mathworks.com/matlab/code/2013/relationships/document"/>
</Relationships>"""


def doc(code):
    return ('<?xml version="1.0" encoding="UTF-8" standalone="no" ?><w:document '
            'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p>'
            '<w:pPr><w:pStyle w:val="code"/></w:pPr><w:r><w:t><![CDATA[' + code + ']]></w:t></w:r></w:p></w:body></w:document>')


def write(path, parts):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for name, text in parts:
            z.writestr(name, text.encode("utf-8"))


code = open(SRC, encoding="utf-8").read()
# 1) minimal OPC: content types + rels + document.xml
write(os.path.join(HERE, "build", "min", "ProbeApp.mlapp"),
      [("[Content_Types].xml", CT), ("_rels/.rels", RELS), ("matlab/document.xml", doc(code))])
# 2) bare zip: document.xml only, class renamed
code2 = code.replace("ProbeApp", "ProbeBare")
write(os.path.join(HERE, "build", "bare", "ProbeBare.mlapp"), [("matlab/document.xml", doc(code2))])
print("ok")
