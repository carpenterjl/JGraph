"""Writes an Excel add-in workbook (.xlam) around a .bas module with no Excel (compiler and SDK plan,
stage K17e, decision KO12): the spike that proved Excel accepts a from-scratch Office Open XML
package whose vbaProject.bin is a compound file (MS-CFB) holding a source-only VBA project
(MS-OVBA), and the way record-k17e.ps1 wraps R2025b's k17bas.bas so Excel's cells can be recorded
over R2025b's add-in (K17a measured that Excel refuses the .bas import without the trust setting).
JGraph's own writer is JGraph.Deploy's ExcelAddInWorkbook, VbaProject and CompoundFile; this script
is the reference it was written from, kept standalone (no olefile).

    python k17xlam.py <out.xlam> <module.bas> [ModuleName]
"""
import struct
import sys
import uuid
import zipfile

# ---------------------------------------------------------------- MS-OVBA 2.4.1 compression


def ovba_compress(data: bytes) -> bytes:
    out = bytearray(b"\x01")
    pos = 0
    while pos < len(data):
        chunk = data[pos:pos + 4096]
        pos += len(chunk)
        comp = _compress_chunk(chunk)
        if len(comp) >= 4096:
            # A raw chunk: flag 0, 4096 bytes (padded).
            out += struct.pack("<H", (4096 - 3) | 0x3000)
            out += chunk + b"\x00" * (4096 - len(chunk))
        else:
            out += struct.pack("<H", ((len(comp) + 2) - 3) | 0x3000 | 0x8000)
            out += comp
    return bytes(out)


def _compress_chunk(chunk: bytes) -> bytes:
    out = bytearray()
    cur = 0
    n = len(chunk)
    while cur < n:
        flag_pos = len(out)
        out.append(0)
        flags = 0
        for bit in range(8):
            if cur >= n:
                break
            bc = 4
            while (1 << bc) < cur:
                bc += 1
            max_len = (0xFFFF >> bc) + 3
            best_len = 0
            best_off = 0
            limit = min(max_len, n - cur)
            if limit >= 3:
                for off in range(1, cur + 1):
                    start = cur - off
                    l = 0
                    while l < limit and chunk[start + l] == chunk[cur + l]:
                        l += 1
                    if l > best_len:
                        best_len = l
                        best_off = off
                        if l == limit:
                            break
            if best_len >= 3:
                out += struct.pack("<H", ((best_off - 1) << (16 - bc)) | (best_len - 3))
                flags |= 1 << bit
                cur += best_len
            else:
                out.append(chunk[cur])
                cur += 1
        out[flag_pos] = flags
    return bytes(out)


def ovba_decompress(data: bytes) -> bytes:
    assert data[0] == 1
    out = bytearray()
    pos = 1
    while pos < len(data):
        hdr = struct.unpack_from("<H", data, pos)[0]
        pos += 2
        size = (hdr & 0x0FFF) + 3
        flag = hdr >> 15
        end = pos + size - 2
        start = len(out)
        if not flag:
            out += data[pos:pos + 4096]
            pos += 4096
            continue
        while pos < end:
            flags = data[pos]
            pos += 1
            for bit in range(8):
                if pos >= end:
                    break
                if not (flags >> bit) & 1:
                    out.append(data[pos])
                    pos += 1
                else:
                    tok = struct.unpack_from("<H", data, pos)[0]
                    pos += 2
                    diff = len(out) - start
                    bc = max(4, (diff - 1).bit_length())
                    lm = 0xFFFF >> bc
                    length = (tok & lm) + 3
                    off = (tok >> (16 - bc)) + 1
                    for _ in range(length):
                        out.append(out[-off])
    return bytes(out)

# ---------------------------------------------------------------- MS-OVBA 2.4.3 project-stream encryption


def ovba_encrypt(project_id: str, data: bytes, seed: int) -> str:
    proj_key = sum(project_id.encode("latin1")) & 0xFF
    version_enc = seed ^ 2
    proj_key_enc = seed ^ proj_key
    out = bytearray([seed, version_enc, proj_key_enc])
    unenc1 = proj_key
    enc1 = proj_key_enc
    enc2 = version_enc
    ignored = (seed & 6) >> 1
    plain = bytes([0x11] * ignored) + struct.pack("<I", len(data)) + data
    for b in plain:
        byte_enc = b ^ ((enc2 + unenc1) & 0xFF)
        out.append(byte_enc)
        enc2 = enc1
        enc1 = byte_enc
        unenc1 = b
    return out.hex().upper()

# ---------------------------------------------------------------- MS-CFB writer (v3, 512-byte sectors)

FREESECT = 0xFFFFFFFF
ENDOFCHAIN = 0xFFFFFFFE
FATSECT = 0xFFFFFFFD


class Entry:
    def __init__(self, name, kind, data=None):
        self.name = name
        self.kind = kind  # 1 storage, 2 stream, 5 root
        self.data = data or b""
        self.children = []
        self.id = None
        self.left = self.right = self.child = FREESECT
        self.start = ENDOFCHAIN
        self.size = 0


def _build_tree(entries):
    entries = sorted(entries, key=lambda e: (len(e.name), e.name.upper()))

    def build(lo, hi):
        if lo >= hi:
            return FREESECT
        mid = (lo + hi) // 2
        e = entries[mid]
        e.left = build(lo, mid)
        e.right = build(mid + 1, hi)
        return e.id

    return build(0, len(entries))


def cfb_write(root: Entry) -> bytes:
    flat = []

    def number(e):
        e.id = len(flat)
        flat.append(e)
        for c in e.children:
            number(c)

    number(root)
    for e in flat:
        if e.children:
            e.child = _build_tree(e.children)

    sector = 512
    mini = 64
    mini_stream = bytearray()
    minifat = []
    big = []
    for e in flat:
        if e.kind != 2:
            continue
        e.size = len(e.data)
        if e.size == 0:
            e.start = ENDOFCHAIN
        elif e.size < 4096:
            first = len(mini_stream) // mini
            count = (e.size + mini - 1) // mini
            mini_stream += e.data + b"\x00" * (count * mini - e.size)
            for k in range(count):
                minifat.append(first + k + 1 if k + 1 < count else ENDOFCHAIN)
            e.start = first
        else:
            big.append(e)

    dir_sectors = max(1, (len(flat) * 128 + sector - 1) // sector)
    fat = []
    sectors = []

    def add_chain(data):
        count = (len(data) + sector - 1) // sector
        if count == 0:
            return ENDOFCHAIN
        first = len(sectors)
        for k in range(count):
            sectors.append(data[k * sector:(k + 1) * sector].ljust(sector, b"\x00"))
            fat.append(first + k + 1 if k + 1 < count else ENDOFCHAIN)
        return first

    dir_start = add_chain(b"\x00" * (dir_sectors * sector))
    minifat_bytes = b"".join(struct.pack("<I", v) for v in minifat)
    minifat_start = add_chain(minifat_bytes)
    minifat_count = (len(minifat_bytes) + sector - 1) // sector
    root.start = add_chain(bytes(mini_stream))
    root.size = len(mini_stream)
    for e in big:
        e.start = add_chain(e.data)
    fat_count = 0
    while True:
        needed = (len(fat) + fat_count + 127) // 128
        if needed == fat_count:
            break
        fat_count = needed
    fat_start = len(sectors)
    for k in range(fat_count):
        fat.append(FATSECT)
    while len(fat) % 128:
        fat.append(FREESECT)
    fat_bytes = b"".join(struct.pack("<I", v) for v in fat)
    for k in range(fat_count):
        sectors.append(fat_bytes[k * sector:(k + 1) * sector])

    directory = bytearray()
    for e in flat:
        name16 = (e.name + "\x00").encode("utf-16-le")
        rec = bytearray(128)
        rec[0:len(name16)] = name16
        struct.pack_into("<H", rec, 64, len(name16))
        rec[66] = e.kind
        rec[67] = 1
        struct.pack_into("<III", rec, 68, e.left, e.right, e.child)
        struct.pack_into("<I", rec, 116, e.start if e.kind in (2, 5) else 0)
        struct.pack_into("<Q", rec, 120, e.size if e.kind in (2, 5) else 0)
        directory += rec
    while len(directory) < dir_sectors * sector:
        rec = bytearray(128)
        struct.pack_into("<III", rec, 68, FREESECT, FREESECT, FREESECT)
        directory += rec
    for k in range(dir_sectors):
        sectors[dir_start + k] = bytes(directory[k * sector:(k + 1) * sector])

    header = bytearray(512)
    header[0:8] = bytes.fromhex("D0CF11E0A1B11AE1")
    struct.pack_into("<HHHHH", header, 24, 0x003E, 0x0003, 0xFFFE, 9, 6)
    struct.pack_into("<I", header, 44, fat_count)
    struct.pack_into("<I", header, 48, dir_start)
    struct.pack_into("<I", header, 56, 4096)
    struct.pack_into("<I", header, 60, minifat_start if minifat_count else ENDOFCHAIN)
    struct.pack_into("<I", header, 64, minifat_count)
    struct.pack_into("<I", header, 68, ENDOFCHAIN)
    struct.pack_into("<I", header, 72, 0)
    for k in range(109):
        struct.pack_into("<I", header, 76 + 4 * k, fat_start + k if k < fat_count else FREESECT)
    assert fat_count <= 109
    return bytes(header) + b"".join(sectors)

# ---------------------------------------------------------------- the VBA project


def _rec(rid, body):
    return struct.pack("<HI", rid, len(body)) + body


def _u16(s):
    return s.encode("utf-16-le")


THISWORKBOOK = (
    'Attribute VB_Name = "ThisWorkbook"\r\n'
    'Attribute VB_Base = "0{00020819-0000-0000-C000-000000000046}"\r\n'
    'Attribute VB_GlobalNameSpace = False\r\n'
    'Attribute VB_Creatable = False\r\n'
    'Attribute VB_PredeclaredId = True\r\n'
    'Attribute VB_Exposed = True\r\n'
    'Attribute VB_TemplateDerived = False\r\n'
    'Attribute VB_Customizable = True\r\n'
)


def vba_project(module_name: str, module_source: str, project_name="VBAProject") -> bytes:
    modules = [("ThisWorkbook", THISWORKBOOK, 0x0022),
               (module_name, f'Attribute VB_Name = "{module_name}"\r\n' + module_source, 0x0021)]
    d = bytearray()
    d += _rec(0x0001, struct.pack("<I", 3))          # SysKind Win64
    d += _rec(0x0002, struct.pack("<I", 0x409))      # LCID
    d += _rec(0x0014, struct.pack("<I", 0x409))      # LCIDINVOKE
    d += _rec(0x0003, struct.pack("<H", 1252))       # code page
    d += _rec(0x0004, project_name.encode("latin1"))
    d += _rec(0x0005, b"") + _rec(0x0040, b"")       # doc string
    d += _rec(0x0006, b"") + _rec(0x003D, b"")       # help file
    d += _rec(0x0007, struct.pack("<I", 0))          # help context
    d += _rec(0x0008, struct.pack("<I", 0))          # lib flags
    d += struct.pack("<HIIH", 0x0009, 4, 1, 0)       # version
    d += _rec(0x000C, b"") + _rec(0x003C, b"")       # constants
    for name, libid in [
        ("stdole", r"*\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\Windows\System32\stdole2.tlb#OLE Automation"),
        ("Office", r"*\G{2DF8D04C-5BFA-101B-BDE5-00AA0044DE52}#2.0#0#C:\Program Files\Common Files\Microsoft Shared\OFFICE16\MSO.DLL#Microsoft Office 16.0 Object Library"),
    ]:
        d += _rec(0x0016, name.encode("latin1")) + _rec(0x003E, _u16(name))
        lib = libid.encode("latin1")
        d += _rec(0x000D, struct.pack("<I", len(lib)) + lib + b"\x00" * 6)
    d += _rec(0x000F, struct.pack("<H", len(modules)))
    d += _rec(0x0013, struct.pack("<H", 0xFFFF))
    for name, src, kind in modules:
        d += _rec(0x0019, name.encode("latin1")) + _rec(0x0047, _u16(name))
        d += _rec(0x001A, name.encode("latin1")) + _rec(0x0032, _u16(name))
        d += _rec(0x001C, b"") + _rec(0x0048, b"")
        d += _rec(0x0031, struct.pack("<I", 0))
        d += _rec(0x001E, struct.pack("<I", 0))
        d += _rec(0x002C, struct.pack("<H", 0xFFFF))
        d += _rec(kind, b"")
        d += _rec(0x002B, b"")
    d += _rec(0x0010, b"")

    project_id = "{" + str(uuid.uuid5(uuid.NAMESPACE_URL, "k17xlam:" + module_name)).upper() + "}"
    proj = [f'ID="{project_id}"']
    for name, _, kind in modules:
        proj.append(f"Document={name}/&H00000000" if kind == 0x0022 else f"Module={name}")
    proj += [f'Name="{project_name}"', 'HelpContextID="0"', 'VersionCompatible32="393222000"',
             f'CMG="{ovba_encrypt(project_id, bytes(4), 0x58)}"',
             f'DPB="{ovba_encrypt(project_id, bytes(1), 0x5A)}"',
             f'GC="{ovba_encrypt(project_id, bytes([0xFF]), 0x5C)}"',
             "", "[Host Extender Info]", "&H00000001={3832D640-CF90-11CF-8E43-00A0C911005A};VBE;&H00000000", "", "[Workspace]"]
    for name, _, _ in modules:
        proj.append(f"{name}=0, 0, 0, 0, C")
    project_text = ("\r\n".join(proj) + "\r\n").encode("latin1")
    wm = b"".join(name.encode("latin1") + b"\x00" + _u16(name) + b"\x00\x00" for name, _, _ in modules) + b"\x00\x00"

    root = Entry("Root Entry", 5)
    vba = Entry("VBA", 1)
    vba.children.append(Entry("dir", 2, ovba_compress(bytes(d))))
    vba.children.append(Entry("_VBA_PROJECT", 2, bytes.fromhex("CC61FFFF000000")))
    for name, src, _ in modules:
        vba.children.append(Entry(name, 2, ovba_compress(src.encode("latin1"))))
    root.children.append(vba)
    root.children.append(Entry("PROJECT", 2, project_text))
    root.children.append(Entry("PROJECTwm", 2, wm))
    return cfb_write(root)

# ---------------------------------------------------------------- the package

CONTENT_TYPES = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
                 '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                 '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                 '<Default Extension="xml" ContentType="application/xml"/>'
                 '<Default Extension="bin" ContentType="application/vnd.ms-office.vbaProject"/>'
                 '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.ms-excel.addin.macroEnabled.main+xml"/>'
                 '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
                 '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>'
                 '</Types>')
RELS = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>'
        '</Relationships>')
WORKBOOK = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
            '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
            '<fileVersion appName="xl" lastEdited="7" lowestEdited="7" rupBuild="26730"/>'
            '<workbookPr codeName="ThisWorkbook"/>'
            '<bookViews><workbookView visibility="veryHidden" xWindow="0" yWindow="0" windowWidth="20000" windowHeight="10000"/></bookViews>'
            '<sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets>'
            '<calcPr calcId="191029"/>'
            '</workbook>')
WORKBOOK_RELS = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
                 '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
                 '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>'
                 '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>'
                 '<Relationship Id="rId3" Type="http://schemas.microsoft.com/office/2006/relationships/vbaProject" Target="vbaProject.bin"/>'
                 '</Relationships>')
SHEET = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
         '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
         '<dimension ref="A1"/><sheetViews><sheetView workbookViewId="0"/></sheetViews>'
         '<sheetFormatPr defaultRowHeight="15"/><sheetData/>'
         '<pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>'
         '</worksheet>')
STYLES = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
          '<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
          '<fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>'
          '<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>'
          '<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>'
          '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>'
          '<cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>'
          '<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>'
          '</styleSheet>')


def write_xlam(path: str, module_name: str, bas_text: str) -> bytes:
    vba = vba_project(module_name, bas_text)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", CONTENT_TYPES)
        z.writestr("_rels/.rels", RELS)
        z.writestr("xl/workbook.xml", WORKBOOK)
        z.writestr("xl/_rels/workbook.xml.rels", WORKBOOK_RELS)
        z.writestr("xl/worksheets/sheet1.xml", SHEET)
        z.writestr("xl/styles.xml", STYLES)
        z.writestr("xl/vbaProject.bin", vba)
    return vba


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(2)
    out, bas = sys.argv[1], sys.argv[2]
    name = sys.argv[3] if len(sys.argv) > 3 else "Module1"
    text = open(bas, encoding="latin1", newline="").read()
    vba = write_xlam(out, name, text)
    print(f"wrote {out}: vbaProject.bin {len(vba)} bytes, module {name} {len(text)} chars")
