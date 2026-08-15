"""Builds the version 7.3 (HDF5) MAT-files the reader tests read, and emits them as C# source.

MATLAB writes a 512-byte userblock carrying the same description text a version 5 file starts
with, then a plain HDF5 file. Variables are root-level datasets or groups, each tagged with a
MATLAB_class attribute; because HDF5 is row-major and MATLAB is column-major, an m-by-n matrix
is stored with its dimensions reversed, which is also why the stored run of elements is already
in MATLAB's own column-major order.

The point of generating these with a real HDF5 library is that nothing here shares an opinion
with the reader under test. Run it as:

    python tools/make-v73-fixtures.py tests/JGraph.Tests/Scripting/MatV73Fixture.cs

It needs h5py and numpy, which are not otherwise dependencies of this project; the generated
file is checked in, so the tests never need them.
"""

import base64
import os
import sys
import tempfile
import zlib

import h5py
import numpy as np

WHERE = tempfile.mkdtemp(prefix="jgraph-v73-")


def userblock(path):
    """Stamps the version 5 style header MATLAB puts in front of the HDF5 bytes."""
    text = ("MATLAB 7.3 MAT-file, Platform: PCWIN64, Created on: "
            "Sat Aug 15 12:00:00 2026 HDF5 schema 1.00 .").encode("ascii")
    block = bytearray(b"\0" * 512)
    block[0:len(text)] = text
    block[124:126] = (0x0200).to_bytes(2, "big")
    block[126:128] = b"IM"
    with open(path, "r+b") as handle:
        handle.write(bytes(block))


def new(name):
    path = os.path.join(WHERE, name)
    if os.path.exists(path):
        os.remove(path)
    return path


def tag(obj, cls, **extra):
    obj.attrs.create("MATLAB_class", np.bytes_(cls.encode("ascii")))
    for key, value in extra.items():
        obj.attrs.create(key, np.array(value, dtype=np.int32))


def matrix(f, name, data, cls, **extra):
    """Stores a MATLAB matrix: reversed dimensions, because HDF5 counts the other way."""
    array = np.asarray(data)
    ds = f.create_dataset(name, data=array.T if array.ndim > 1 else array)
    tag(ds, cls, **extra)
    return ds


def field_names(names):
    """MATLAB_fields is an array of variable-length character sequences, not of strings."""
    out = np.empty(len(names), dtype=object)
    for i, name in enumerate(names):
        out[i] = np.frombuffer(name, dtype="S1")
    return out


def plain():
    path = new("v73_plain.mat")
    with h5py.File(path, "w", userblock_size=512) as f:
        matrix(f, "A", np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]]), "double")
        matrix(f, "s", np.array([[42.5]]), "double")
        matrix(f, "n", np.array([[1, 2, 3]], dtype=np.int32), "int32")
        matrix(f, "g", np.array([[1.5, 2.5]], dtype=np.float32), "single")
        matrix(f, "L", np.array([[1, 0], [0, 1]], dtype=np.uint8), "logical",
               MATLAB_int_decode=1)
        ds = f.create_dataset("t", data=np.array([ord(c) for c in "hello"], dtype=np.uint16))
        tag(ds, "char", MATLAB_int_decode=2)
        # A char matrix is two rows of three, so on disk it is three-by-two.
        ds = f.create_dataset("T", data=np.array([[ord(c) for c in "adx"],
                                                  [ord(c) for c in "bey"]],
                                                 dtype=np.uint16).T)
        tag(ds, "char", MATLAB_int_decode=2)
        matrix(f, "N", np.arange(1.0, 13.0).reshape(2, 3, 2), "double")
        # An empty holds its own dimensions rather than any data.
        ds = f.create_dataset("E", data=np.array([0, 0], dtype=np.uint64))
        tag(ds, "double", MATLAB_empty=1)
    userblock(path)
    return path


def compressed():
    path = new("v73_deflate.mat")
    with h5py.File(path, "w", userblock_size=512) as f:
        big = np.arange(1.0, 721.0).reshape(24, 30)
        ds = f.create_dataset("B", data=big.T, chunks=(10, 8), compression="gzip",
                              compression_opts=9)
        tag(ds, "double")
        shuffled = np.arange(1.0, 145.0).reshape(12, 12)
        ds = f.create_dataset("S", data=shuffled.T, chunks=(5, 5), shuffle=True,
                              compression="gzip", fletcher32=True)
        tag(ds, "double")
    userblock(path)
    return path


def complexes():
    path = new("v73_complex.mat")
    with h5py.File(path, "w", userblock_size=512) as f:
        pair = np.dtype([("real", "<f8"), ("imag", "<f8")])
        data = np.zeros((2,), dtype=pair)
        data["real"] = [1.0, 3.0]
        data["imag"] = [2.0, -4.0]
        ds = f.create_dataset("z", data=data)
        tag(ds, "double", MATLAB_complex=1)
    userblock(path)
    return path


def nested():
    path = new("v73_nested.mat")
    with h5py.File(path, "w", userblock_size=512) as f:
        refs = f.create_group("#refs#")
        # A cell array is a matrix of object references into #refs#.
        first = refs.create_dataset("a", data=np.array([[7.0]]))
        tag(first, "double")
        second = refs.create_dataset("b", data=np.array([ord(c) for c in "two"],
                                                        dtype=np.uint16))
        tag(second, "char", MATLAB_int_decode=2)
        cell = f.create_dataset("C", data=np.array([first.ref, second.ref],
                                                   dtype=h5py.ref_dtype))
        tag(cell, "cell")

        group = f.create_group("St")
        tag(group, "struct")
        field = group.create_dataset("alpha", data=np.array([[1.0, 2.0]]).T)
        tag(field, "double")
        field = group.create_dataset("beta", data=np.array([ord(c) for c in "hi"],
                                                           dtype=np.uint16))
        tag(field, "char", MATLAB_int_decode=2)
        group.attrs.create("MATLAB_fields", field_names([b"alpha", b"beta"]),
                           dtype=h5py.vlen_dtype(np.dtype("S1")))

        # A struct array stores each field as one reference per element.
        arr = f.create_group("Sa")
        tag(arr, "struct")
        cells = []
        for i, value in enumerate([10.0, 20.0]):
            item = refs.create_dataset(f"e{i}", data=np.array([[value]]))
            tag(item, "double")
            cells.append(item.ref)
        arr.create_dataset("v", data=np.array(cells, dtype=h5py.ref_dtype))
        arr.attrs.create("MATLAB_fields", field_names([b"v"]),
                         dtype=h5py.vlen_dtype(np.dtype("S1")))
    userblock(path)
    return path


def latest():
    path = new("v73_latest.mat")
    with h5py.File(path, "w", userblock_size=512, libver="latest") as f:
        matrix(f, "A", np.array([[1.0, 2.0], [3.0, 4.0]]), "double")
        ds = f.create_dataset("t", data=np.array([ord(c) for c in "new"], dtype=np.uint16))
        tag(ds, "char", MATLAB_int_decode=2)
    userblock(path)
    return path


def unreadable():
    path = new("v73_object.mat")
    with h5py.File(path, "w", userblock_size=512) as f:
        ds = f.create_dataset("obj", data=np.array([[1.0]]))
        tag(ds, "myclass")
        ds = f.create_dataset("str", data=np.array([[1.0]]))
        tag(ds, "string")
        matrix(f, "ok", np.array([[5.0]]), "double")
    userblock(path)
    return path


HEADER = '''// Generated by tools/make-v73-fixtures.py — do not edit by hand.
using System.IO;
using System.IO.Compression;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Real version 7.3 MAT-files for the reader tests, each written by an HDF5 library and kept here
/// deflated and base64-encoded so the repository carries no binary assets.
/// </summary>
/// <remarks>
/// Hand-building these the way the version 5 tests hand-build their big-endian files was the
/// alternative, and was rejected: a hand-rolled writer agreeing with a hand-rolled reader proves
/// only that the two share an opinion. These bytes were laid out by something that had never heard
/// of this reader.
/// </remarks>
internal static class MatV73Fixture
{'''


def emit(files, target):
    lines = [HEADER]
    entries = []
    for path, note in files:
        raw = open(path, "rb").read()
        text = base64.b64encode(zlib.compress(raw, 9)).decode("ascii")
        name = os.path.basename(path)
        field = "".join(part.capitalize()
                        for part in os.path.splitext(name)[0][len("v73_"):].split("_"))
        lines.append("    /// <summary>%s</summary>" % note)
        lines.append("    private const string %s =" % field)
        chunks = [text[i:i + 110] for i in range(0, len(text), 110)]
        for i, chunk in enumerate(chunks):
            lines.append('        "%s"%s' % (chunk, ";" if i == len(chunks) - 1 else " +"))
        lines.append("")
        entries.append((name, field))

    lines.append('    /// <summary>Writes one fixture into <paramref name="folder"/>'
                 ' and returns its path.</summary>')
    lines.append("    public static string Write(string folder, string name)")
    lines.append("    {")
    lines.append("        string packed = name switch")
    lines.append("        {")
    for name, field in entries:
        lines.append('            "%s" => %s,' % (name, field))
    lines.append('            _ => throw new ArgumentException('
                 '$"There is no fixture named {name}.", nameof(name)),')
    lines.append("        };")
    lines.append("")
    lines.append("        string path = Path.Combine(folder, name);")
    lines.append("        using var source = new MemoryStream(Convert.FromBase64String(packed));")
    lines.append("        using var zlib = new ZLibStream(source, CompressionMode.Decompress);")
    lines.append("        using var file = new FileStream(path, FileMode.Create);")
    lines.append("        zlib.CopyTo(file);")
    lines.append("        return path;")
    lines.append("    }")
    lines.append("}")
    with open(target, "w", newline="\r\n") as out:
        out.write("\n".join(lines) + "\n")
    print("wrote %s" % target)


if __name__ == "__main__":
    emit([
        (plain(), "Every plain kind: double, scalar, int32, single, logical, char, a char matrix, "
                  "a three-dimensional array, and an empty."),
        (compressed(), "Two chunked datasets, one deflated and one shuffled, deflated and "
                       "checksummed, each spanning several chunks in both directions."),
        (complexes(), "A complex row vector, stored as the two-member compound MATLAB uses."),
        (nested(), "A cell array of references, a scalar struct, and a two-element struct array."),
        (latest(), "The newer HDF5 layout: a version 3 superblock, version 2 object headers, and "
                   "links held as header messages rather than in a symbol table."),
        (unreadable(), "Two classes with no representation here and one ordinary double beside "
                       "them, so that a named load can step over what it was not asked for."),
    ], sys.argv[1] if len(sys.argv) > 1 else "MatV73Fixture.cs")
