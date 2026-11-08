"""Compares JGraph's K17 recordings with R2025b's (compiler and SDK plan, stage K17).

    python compare-k17.py <folder holding JGraph's k17_*.out.txt> [--only excel,com_preinit,com,com_extra,cells,mps,payload,pe,strings,registry]

Each recording is key<TAB>value lines with a display, a file's text or what a call printed indented
under its key (record-k17.ps1 writes R2025b's into recordings/; the later stages write JGraph's with
the same names into a folder of their own). Every key of R2025b's is one of: agrees, a recorded
divergence (named below with its reason and the ADR that records it), or unexplained. The exit code
is the number unexplained. Stage K17a wrote the recordings and this comparer; the KNOWN lists fill
as K17b to K17g land. Run against recordings/ itself, every key agrees.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REC = HERE / "recordings"

FILES = ["excel", "com_preinit", "com", "com_extra", "cells", "hosting", "mps", "payload", "pe", "strings", "registry"]

# The keys whose JGraph answer differs by design, per recording, by pattern (the first matching
# pattern's reason). Timings and the build's own byte counts never agree.
KNOWN = {
    "excel": [
        (r"^.*\.seconds$", "a timing"),
        (r"^(matlab\.version|compiler\.version|mbuild\.cpp)$", "the engine's own version; JGraph's build needs no C++ compiler (KO9)"),
        # Stage K17b (ADR 0315): the options and mcc -W excel; the client is K17f's.
        (r"^derived\.saveload$", "an options object is not saved to a MAT-file: its on/off properties are .NET values (open item 215)"),
        (r"^set\.SupportPackages\.\d+$", "JGraph has no support packages, so a list of them is refused in JGraph's own words (stage K6; ADR 0315)"),
        (r"^set\.AdditionalFiles\.4$", "AdditionalFiles set to '' is kept as {} where R2025b keeps the '' itself (ADR 0315)"),
        (r"^opts\.superclasses$", "JGraph's options classes declare no superclasses; DependencyOptions and AllOptions are R2025b's own (ADR 0315)"),
        (r"^opts\.(default|properties)$|^.*\.(results|Options|RuntimeDependencies)$|^set\.(FunctionFiles|AdditionalFiles)\.\d+$",
         "an object's or a cell column's display is JGraph's own layout, not R2025b's aligned one (open item 194), and JGraph's runtime is one install, listed alone where R2025b lists its add-ons (ADR 0267)"),
        (r"^.*\.file\.(readme\.txt|GettingStarted\.html)$", "readme.txt and GettingStarted.html are in JGraph's words, for the JGraph Runtime (ADR 0315)"),
        (r"^.*\.file\.(buildresult\.json|requiredMCRProducts\.txt)$", "JGraph's runtime is one install (35010), listed alone where R2025b lists its add-ons (ADR 0267)"),
        # Stage K17c (ADR 0316): the DLL from the template, stamped and registered.
        (r"^.*\.file\..*\.(cpp|hpp|idl|def|rc)$|^.*\.file\.dlldata\.c$|^mcc_noversion\.idl$|^mcc_version2\.rc$",
         "no C++ source, IDL, module definition or resource script is generated: the add-in's DLL is the prebuilt template jgxl.dll stamped by the build with its type library and archive (decision KO9; ADR 0316)"),
        (r"^.*\.file\..*\.bas$|^mcc_twofiles\.bas$",
         "the module is R2025b's to the character but for two things: the utility library's ProgID is JGraph's (JGraphComUtil.MWUtil25.2, decision KO10), and the objects are created with no server name, since R2025b's CreateObject(progid, \"localhost\") is a DCOM activation that cannot reach an add-in registered for the current user (k17_cells, k17_hosting; ADR 0318)"),
        (r"^.*\.registry\.Software/Classes/TypeLib$",
         "JGraph registers its 64-bit type library alone (a win64 entry) where R2025b's MWRegsvr writes a win32 entry beside it (ADR 0316)"),
        (r"^.*\.registry\.Software/Classes/Interface$",
         "the add-in's own type library declares the three flags interfaces too (JGraph's ids, MathWorks' member names), so four Interface keys are registered where R2025b's library imports MathWorks' mwcomutil.tlb and registers one (decision KO10; ADR 0316; stage K17d's utility library may move them)"),
        (r"^guids\.", "JGraph writes no IDL to read ids from; its ids are its own derivation from the names and the version (ADR 0316)"),
        (r"^plain\.registry\.Software/Classes/CLSID$", "the first build registers JGraph's COM utility library (its nine classes) beside the add-in when it finds it unregistered, as MATLAB's installer registers mwcomutil.dll (ADR 0317)"),
        (r"^mcc_(compile|badname|badclass)(\..*)?$",
         "R2025b's build fails in mbuild (-T compile:lib; a name that is not an identifier); JGraph refuses such a name before writing anything and takes -T compile:lib as -T codegen (ADR 0315)"),
        (r"^mcc_\w+$|^.*\.(printed|warn)$",
         "R2025b prints mbuild's transcript and, for a -b build, warns COM_WARN_FAILED_CREATE_EXCEL_ADDIN where Excel does not trust its VBA project; JGraph writes the add-in workbook as a file (.xlam) with no Excel and no warning (decision KO12; ADR 0318); its per-user registration warning names MATLAB where JGraph's names JGraph (ADR 0316)"),
        (r"^.*\.(files|Files)$",
         "mbuild's C++ sources, IDL, MIDL outputs, module definition, resource script, import library and exports file are not written (KO9), JGraph's .xlam is listed where R2025b's .xla would stand (KO12; ADR 0318), and the DLL's byte count is the template's (ADR 0316)"),
        (r"^(copts|cset|cbuild|popts|pset|pkg)\.|^(fromresults|fromfiles|cdefaults|nobas|cdebug|cv2|cv05|cvtext|blank|date|nan|numdate|blankdate|allswitches|onefileall|cpfile|mpsxl_\w+|built)(\..*)?$",
         "the Excel client for the Production Server is stage K17f's; JGraph's probe run stops after the add-in sections until then (run-k17.ps1 -UpTo addin)"),
    ],
    "com_preinit": [
        (r"^(create\.)?seconds$", "a timing"),
        (r"^host\.", "the client host's own versions"),
        (r"^preinit\.init\.dictionary$", "an object that is no Excel object is refused with JGraph's text (its Application property is read); R2025b refuses it with none (ADR 0317)"),
        (r"^preinit\.call", "JGraph's add-in runs before MWInitApplication: it needs no Excel Application to call its functions, where R2025b's refuses every call until it has one (ADR 0316)"),
    ],
    "com": [
        (r"^(create\.)?seconds$", "a timing"),
        (r"^host\.", "the client host's own versions"),
        (r"^excel\.version$", "the oracle's own Excel, or none under -NoExcel"),
        # Stage K17d (ADR 0317): the utility library, the flags' effect, Range objects and the value classes.
        (r"^util\.MWInitApplicationWithMCROptions$", "the refusal names the JGraph Runtime and JGraph's utility ProgID as its source (JGraphComUtil.MWUtil25.2, decision KO10; ADR 0317)"),
        (r"^util\.CreateRemoteObject$", "JGraph's CreateRemoteObject makes the object of the ProgID in this process; R2025b's asks DCOM for a remote one and fails with 'Class not registered' (ADR 0317)"),
        (r"^in\.complex_noimag$", "a complex value whose imaginary part is zero is real to JGraph's engine (ADR 0266); R2025b keeps 1+0i (ADR 0317)"),
        (r"^in\.flags_object$", "a flags object passed as an argument is refused with JGraph's text; R2025b refuses it with none (ADR 0317)"),
        (r"^out\.sparselogical$", "JGraph's engine holds a sparse matrix as double (sparsity is an attribute; no logical sparse), so MWSparse's Array is Double() where R2025b's is Boolean() (ADR 0317)"),
        (r"^rangeout\.datetime$", "a datetime output is refused with a message naming what to return instead; R2025b writes nothing into the Range and raises no error (ADR 0316, ADR 0317)"),
        (r"^flag\.(OutputAsDate\.(False|True)\.datetime|DateBias\.\d+\.datetimeout)$", "a datetime output is refused with a message naming what to return instead; R2025b answers a type mismatch value (ADR 0316)"),
        (r"^flag\.InputArrayIndFlag\.\d$", "the engine's message for + over two cells is JGraph's own ('Operator + needs numbers...'); R2025b's is 'Invalid data type. Argument must be numeric, char, or logical.' (open item 247)"),
        (r"^help\.k17_h1$", "the H1 line is answered as written; R2025b's help map widens its UTF-8 as Windows-1252 and eats '% o' as a format (ADR 0314, a MATLAB bug not copied)"),
        (r"^opt\.(middle|emptymiddle|paramnotfound)$|^err\.(defaultmiddle|emptymiddle|unconvertible|nooutputs\.fail)$|^stale\.(2|3|5|7)\.",
         "a refusal of the call's own carries JGraph's text (which argument was left out, what cannot be converted) where R2025b's carries none, or the last MATLAB error's text (ADR 0316)"),
        (r"^(in|pass)\.i8$|^out\.(int64|uint64)$",
         "int64 and uint64 cross as VT_I8 and VT_UI8 where JGraph holds them exactly (to 2^53, ADR 0266) and are refused with a message where it cannot; R2025b refuses an int64 in with no text and answers a type mismatch out (ADR 0316)"),
        (r"^in\.arr_r8_empty(2d)?$|^in\.arr_var_empty$", "an empty array crosses as an empty double of its shape; R2025b refuses an empty double array with no text and makes an empty Variant array int16 (ADR 0316)"),
        (r"^in\.(dispatch_other|flags_object)$", "an object JGraph cannot convert is refused with a message; R2025b refuses it with none (ADR 0316)"),
        (r"^out\.string", "a string answers its text, a string array an array of them; R2025b answers a type mismatch (ADR 0316)"),
        (r"^out\.(datetime.*|duration|categorical|fhandle.*|table|map|object)$",
         "a value no VARIANT holds is refused with a message naming what to return instead; R2025b answers a type mismatch value (ADR 0316; a datetime may become a DATE in stage K17d)"),
        (r"^out\.complexzero$", "a complex value whose imaginary part is zero is real to JGraph's engine (ADR 0266) and crosses as a double; R2025b answers an MWComplex (ADR 0316)"),
    ],
    "com_extra": [
        (r"^seconds$", "a timing"),
        (r"^host\.", "the client host's own versions"),
        (r"^(create\.)?seconds$", "a timing"),
        (r"^excel\.version$", "the oracle's own Excel"),
        (r"^x\.Coerce\.(8|9|10|12|13)\.naninf$", "a NaN coerced to an integer class is 0, MATLAB's own int8(NaN); R2025b's conversion answers the type's minimum (ADR 0317, a MATLAB-wrong row)"),
        (r"^x\.Coerce\.7\.naninf$", "a NaN or an infinity coerced to single stays NaN or Inf; R2025b's conversion answers -FLT_MAX and FLT_MAX (ADR 0317, a MATLAB-wrong row)"),
        (r"^x\.Coerce\.4\.naninf$", "a NaN coerced to char is 'NaN'; R2025b's is the C runtime's '-nan(ind)' (ADR 0317)"),
        (r"^x\.Coerce\.8\.ui1$", "a uint8 200 coerced to int8 saturates to 127, MATLAB's own int8(uint8(200)); R2025b's conversion answers 0 (ADR 0317, a MATLAB-wrong row)"),
        (r"^x\.ReplaceMissing\.\d\.empty_middle$|^x\.(complex\.unset_middle|sparse\.rows_only_middle)$", "a refusal of the call's own carries JGraph's text (which argument was left out); R2025b's carries none, or the last MATLAB error's text (ADR 0316)"),
        (r"^x\.util\.isjvm_type$", "IsMCRJVMEnabled is false: JGraph has no JVM; R2025b's is true once its runtime has started (ADR 0317)"),
        (r"^x\.OutputAsDate\.True\.(doublerow|doublemat|mixedrange)$", "a numeric array under OutputAsDate answers dates (or the biased doubles) element by element; R2025b builds an array its own client cannot read (SafeArrayTypeMismatchException; ADR 0317, a MATLAB-wrong row)"),
        (r"^x\.OutputArrayFormat\.2\.(string|int64)$", "a string answers its text and an int64 its value, as under the default format (ADR 0316)"),
        (r"^x\.complex\.ints$", "an MWComplex with integer parts is a double complex value: JGraph keeps no complex integer (ADR 0266)"),
        (r"^x\.complex\.(bool|text|mismatch|mismatch_lengths)$|^x\.sparse\.(variant_indices|int_values|length_mismatch|text_values|2d_array)$|^x\.range\.(worksheet|workbook)$",
         "a value class filled with what it cannot hold, or an object with no value, is refused with JGraph's text; R2025b refuses it with none (ADR 0317)"),
        (r"^x\.struct\.(dims_zero|dims_zero_echo|init_dims_negative|init_names_number)$", "MWStruct's refusals name JGraph's utility ProgID as their source (JGraphComUtil.MWStruct25.2, decision KO10), a non-text field name refused in JGraph's words (ADR 0317)"),
        (r"^x\.sparse\.logical$|^x\.OutputArrayFormat\.2\.sparselogical$", "JGraph's engine holds a sparse matrix as double: a logical MWSparse becomes a sparse double, a sparse logical answers Double() (ADR 0317)"),
        (r"^x\.util\.create_remote_(util|dictionary|empty|versionless)$", "JGraph's CreateRemoteObject makes the object of any registered ProgID in this process; R2025b's asks DCOM and fails for all but its own classes (ADR 0317)"),
        (r"^x\.util\.second_util$", "every CreateObject of JGraphComUtil.MWUtil25.2 is a new object; R2025b's MWUtil is one object per process (ADR 0317)"),
        (r"^x\.util\.unpack_range_larger_target$", "MWUnpack into a Range of several cells writes the array over the range resized from its first cell, as an add-in's output does; R2025b writes the first cell alone (ADR 0317)"),
        (r"^x\.utilflags\.(arrayformat_put|arrayformat_put_shares|dataconv_put|dataconv_defaults|arrayformat_defaults|members\.af|members\.dc|put_wrong_type)$",
         "JGraphComUtil.MWArrayFormatFlags and MWDataConversionFlags can be created on their own and put into a flags object; R2025b registers the classes but its class factory refuses them (E_NOTIMPL; ADR 0317)"),
        (r"^x\.rangeout\.(string|stringarr|int64|bigint)$", "a string, a string array and an int64 are written into the Range as text and a number; R2025b writes nothing (ADR 0316, ADR 0317)"),
        (r"^x\.rangeout\.(datetimearr|duration|fhandle|object)$", "a value no VARIANT holds is refused with a message naming what to return instead; R2025b writes nothing into the Range and raises no error (ADR 0316, ADR 0317)"),
    ],
    "cells": [
        (r"^seconds$", "a timing"),
        (r"^(cell|array|again)\.",
         "both engines' add-ins are registered for the current user, which Excel 16 installed by Click-to-Run does not see (k17_hosting): R2025b's module creates them through a server name, \"localhost\" (VBA error 463, Class not registered on local machine, a DCOM activation that reads the machine's registrations alone), JGraph's with none (429, ActiveX component can't create object); the cells wait for a machine-wide registration (open item 248; ADR 0318)"),
    ],
    "hosting": [
        (r"^seconds$", "a timing"),
        (r"^excel\.\d+$", "the Excel processes' ids"),
        (r"^vba\.(util\.(local|localhost)|regread\.(hkcr|hklm)\.util)$|^ps\.util\.localhost$",
         "JGraph's utility library is registered for the current user by the probe's build (the runtime's MSI registers it machine-wide) where MATLAB's installer registered MWComUtil machine-wide, so Excel and a DCOM activation see MathWorks' and not JGraph's (ADR 0318)"),
        (r"^load\.(mclmcrrt25_2|mwcomutil|mclxlmain25_2|jgrt|jgcomutil)\.(open|dll)$",
         "each runtime's own DLLs loaded into Excel: MATLAB Runtime's mclmcrrt25_2, mwcomutil and mclxlmain25_2, JGraph's jgrt and jgcomutil; every one loads (ADR 0318)"),
        (r"^load\.addin\.(dll|again)$",
         "R2025b's add-in DLL ends Excel's process when loaded into it (0x800706BE; its machine-wide design hosts it in dllhost.exe through \"localhost\"); JGraph's loads, and loads again (ADR 0318)"),
    ],
    "mps": [
        (r"^seconds$", "a timing"),
    ],
    "payload": [
        (r"^.*\.bytes$", "the installer's own size (its template is JGraph's, as K18's)"),
    ],
    "pe": [
        (r"^c\.|^p\.|^x\.|^src\.k17mps", "the Excel client's DLLs (compiler.build and mcc -W mpsxl) and its packaged installers are stage K17f's"),
        (r"^.*\.(file|ERR)$",
         "pe-resources.py lists every file beside a DLL under the label of its stem, one stem's rows overwriting each other: mbuild's import library, exports file and generated sources are not written by JGraph (KO9; ADR 0316)"),
        (r"^.*\.(bytes|image\.bytes|sections|subsystem|overlay\.bytes|machine|certificate|import)$",
         "the DLL is JGraph's template: its own size, sections, subsystem and imports (kernel32, ole32, oleaut32, advapi32; no mclxlmain or mclmcrrt) (KO9; ADR 0316)"),
        (r"^.*\.resource$|^.*\.  starts$",
         "JGraph's resources are language-neutral (lang 0) where R2025b's are English (1033), its type library's bytes and its archive's are its own (ADR 0316)"),
        (r"^.*\.  fixed\.(FileOS|FileType|FileFlags)$|^.*\.  table$|^.*\.  var\.Translation$",
         "the version block's file OS and type are set (VOS_NT_WINDOWS32, VFT_DLL) and its string table language-neutral, where R2025b's are 0 and English, as stage K11's libraries (ADR 0267, ADR 0316)"),
    ],
    "strings": [
        (r".*", "the string heaps are MathWorks' .NET client's; JGraph's client is native (KO9), so this recording is read, not matched"),
    ],
    "registry": [
        (r"^registry\.added\.before\.cleanup$",
         "R2025b's recording was taken after an earlier pass of its cleanup had unregistered its add-ins; JGraph's lists the keys its builds registered, each removed below (ADR 0316)"),
        (r"^(regsvr32\.register|mwregsvr\.useronly)$",
         "regsvr32 registers JGraph's add-in without elevation: its DllRegisterServer registers for the current user when it may not for all (exit 0), where R2025b's needs MWRegsvr /useronly after regsvr32 fails (exit 5) (ADR 0316)"),
        (r"^excel\.processes$", "the oracle's own Excel"),
        (r"^registry\.added$", "JGraph's component category stays registered, as the runtime's installer would leave it (decision KO10; ADR 0316)"),
        (r"^unregister$", "the last DLL unregistered is JGraph's COM utility library, which R2025b's run had no counterpart of (ADR 0317)"),
    ],
}

# Keys JGraph's run cannot have at all (R2025b-only artifacts of the probe), which count neither way.
SKIPPED = {
    "excel": [
        r"^license\.java$",
        # mbuild's MIDL transcript and a warning's second line, which R2025b printed outside any key for
        # the two builds the probe runs outside its capture (nooutdir, ra); JGraph runs no mbuild (KO9).
        r"^\w+\.(idl|acf|h) ?$",
        r"^Applications that run with full administrative privileges cannot access the component\.$",
    ],
    "com_preinit": [],
    "com": [],
    "com_extra": [],
    "cells": [],
    "hosting": [],
    "mps": [],
    "payload": [],
    # Files R2025b's probe folder held that are no add-in's: an extracted runtime and a dll folder.
    "pe": [r"^toolbox\.", r"^dll\."],
    "strings": [],
    "registry": [],
}

KEY = re.compile(r"^[\w.:\-/ ]+$")


def read(path):
    keys = {}
    current = None
    for line in Path(path).read_text(encoding="utf-8").split("\n"):
        if not line.strip():
            continue
        # A key line is key<TAB>value, or a display's key alone; everything else (a display's indented
        # lines, a warning printed while a key ran, standard error) belongs to the key before it.
        if "\t" not in line and not KEY.match(line):
            if current is not None:
                keys[current] = keys[current] + "\n" + line
            continue
        key, _, value = line.partition("\t")
        if key.startswith("STDERR") or key.startswith("[") or key.startswith("  ") or key.startswith("=="):
            if current is not None:
                keys[current] = keys[current] + "\n" + line
            continue
        current = key
        keys[key] = value
    return keys


def canonical(value):
    # A warning's printed text is not compared: R2025b prints it on standard output under whatever key
    # ran last, as [Warning: ...] over one line or several with '> In' stack lines after it, and JGraph
    # prints it on standard error, which the runner appends at the end. The warning each key raised is
    # compared through its .warn row (lastwarn), the channel both engines share.
    out = []
    skipping = False
    for l in value.replace("\x08", "").split("\n"):
        if l.startswith("STDERR\t"):
            continue
        s = l.strip()
        if re.match(r"^\|?\s*\[Warning:", s):
            skipping = not re.search(r"\]\s*$", s)
            continue
        if skipping:
            if re.search(r"\]\s*$", s):
                skipping = False
            continue
        if re.search(r"^\s*\|?\s*(\[> )?In |^\s*\|In |style=\"font-weight:bold\"", l) or not s:
            continue
        out.append(l)
    return "\n".join(out)


def known(name, key):
    for pattern, reason in KNOWN.get(name, []):
        if re.match(pattern, key):
            return reason
    return None


def compare(name, expected_path, actual_path):
    expected = read(expected_path)
    actual = read(actual_path)
    agree, divergent, unexplained = [], [], []
    for key, value in expected.items():
        if any(re.match(p, key) for p in SKIPPED.get(name, [])):
            continue
        mine = actual.get(key)
        if mine is not None and canonical(mine) == canonical(value):
            agree.append(key)
            continue
        reason = known(name, key)
        if reason:
            divergent.append((key, reason))
        else:
            unexplained.append((key, value, mine))
    extra = [k for k in actual if k not in expected]
    return agree, divergent, unexplained, extra


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    folder = Path(argv[1])
    only = FILES
    if "--only" in argv:
        only = argv[argv.index("--only") + 1].split(",")
    total_unexplained = 0
    for name in only:
        expected_path = REC / ("k17_%s.out.txt" % name)
        actual_path = folder / ("k17_%s.out.txt" % name)
        if not expected_path.exists():
            print("== %s: no R2025b recording" % name)
            continue
        if not actual_path.exists():
            print("== %s: JGraph has no %s yet" % (name, actual_path.name))
            continue
        agree, divergent, unexplained, extra = compare(name, expected_path, actual_path)
        print("== %s: %d keys, %d agree, %d recorded divergences, %d unexplained, %d only in JGraph's"
              % (name, len(agree) + len(divergent) + len(unexplained), len(agree), len(divergent), len(unexplained), len(extra)))
        reasons = {}
        for key, reason in divergent:
            reasons.setdefault(reason, []).append(key)
        for reason, keys in reasons.items():
            print("  divergence (%d): %s" % (len(keys), reason))
            print("    " + ", ".join(keys[:12]) + (" ..." if len(keys) > 12 else ""))
        for key, value, mine in unexplained:
            print("  UNEXPLAINED %s" % key)
            print("    R2025b: %s" % value.replace("\n", "\n            "))
            print("    JGraph: %s" % ("(missing)" if mine is None else mine.replace("\n", "\n            ")))
        total_unexplained += len(unexplained)
    return total_unexplained


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    sys.exit(main(sys.argv))
