function out = ix_compile(kind)
% IX_COMPILE  The cases of jgnet_compile (interop plan, stage 6, ADR 0179): jgraph.net.compile, the
%   inline C# helper. It is a JGraph extension with no MATLAB counterpart, so the answers are written
%   from the rule. Answers text: a value, an identifier, or a diagnostic with its folder removed.
switch kind
    case 'static'
        % C# text compiles into an assembly whose types a dotted name reaches at once.
        asm = jgraph.net.compile("namespace IxA { public static class M { public static double Mean3(double a, double b, double c) { return (a + b + c) / 3; } } }");
        out = sprintf('%s %g', class(asm), IxA.M.Mean3(1, 2, 6));
    case 'file'
        % A .cs file is found as a script file is (here: on the path); the build takes its name.
        asm = jgraph.net.compile("ix_compile_helper.cs");
        out = sprintf('%g %s %s', IxCompile.FileHelper.Hypot(3, 4), char(IxCompile.FileHelper.Greet('jg')), ...
            char(asm.AssemblyHandle.GetName().Name));
    case 'same'
        % The same build again answers the same NET.Assembly.
        src = "namespace IxB { public class K { public int V = 7; } }";
        a1 = jgraph.net.compile(src);
        a2 = jgraph.net.compile(src);
        k = IxB.K();
        out = sprintf('%d %d', a1 == a2, k.V);
    case 'recompile'
        % A build under the same name replaces the earlier one: new objects are the new version, and
        % an object of the old one refuses every use.
        jgraph.net.compile("namespace IxC { public class Box { public int Get() { return 1; } } }", AssemblyName="IxC");
        old = IxC.Box();
        jgraph.net.compile("namespace IxC { public class Box { public int Get() { return 2; } } }", AssemblyName="IxC");
        fresh = IxC.Box();
        try
            old.Get();
            refused = 'none';
        catch e
            refused = e.identifier;
        end
        out = sprintf('%d %s', fresh.Get(), refused);
    case 'edited_text'
        % Edited text under its default name replaces the build that defined the same type.
        jgraph.net.compile("namespace IxD { public static class T { public static int V() { return 10; } } }");
        jgraph.net.compile("namespace IxD { public static class T { public static int V() { return 20; } } }");
        out = sprintf('%d', IxD.T.V());
    case 'error'
        % Every error, one per line, in the compiler's own layout.
        try
            jgraph.net.compile("namespace IxE { public class X { public int Y() { return 1 } public int Z() { return q; } } }");
            out = 'none';
        catch e
            lines = splitlines(string(e.message));
            out = sprintf('%s ## %s ## %s ## %s', e.identifier, lines(1), lines(2), lines(3));
        end
    case 'file_error'
        % A file's diagnostics carry its path, so the editor can jump to the line.
        folder = tempname;
        mkdir(folder);
        file = fullfile(folder, 'IxBroken.cs');
        fid = fopen(file, 'w');
        fprintf(fid, 'namespace IxF\n{\n    public class X { int y = "text"; }\n}\n');
        fclose(fid);
        try
            jgraph.net.compile(file);
            out = 'none';
        catch e
            lines = splitlines(string(e.message));
            out = char(strrep(lines(2), folder, '<folder>'));
        end
        delete(file);
        rmdir(folder);
    case 'warning'
        % Compiler warnings are MATLAB warnings.
        lastwarn('');
        jgraph.net.compile("namespace IxG { public class X { public int Y() { int unused; return 1; } } }");
        [msg, id] = lastwarn;
        out = sprintf('%s ## %s', id, msg);
    case 'no_namespace'
        % A public type in no namespace compiles, and the warning says a script cannot name it.
        lastwarn('');
        jgraph.net.compile("public class IxOrphan { }");
        [msg, id] = lastwarn;
        out = sprintf('%s ## %s', id, msg);
    case 'references'
        % One build references another by its assembly name; a framework name is accepted as is.
        jgraph.net.compile("namespace IxH { public static class Base { public static int Two() { return 2; } } }", AssemblyName="IxHBase");
        jgraph.net.compile("namespace IxH { public static class Top { public static int Four() { return Base.Two() * 2; } } }", ...
            AssemblyName="IxHTop", References=["IxHBase", "System.Net.Http"]);
        out = sprintf('%d', IxH.Top.Four());
    case 'bad_reference'
        try
            jgraph.net.compile("namespace IxI { }", References="No.Such.Assembly");
            out = 'none';
        catch e
            out = e.identifier;
        end
    case 'added_assembly'
        % An assembly the session added is referenced without being named, and resolved at run time.
        p = interop_paths();
        NET.addAssembly(p.assembly);
        jgraph.net.compile("namespace IxJ { public static class Use { public static double Tenfold(double x) { return JGTest.Invoker.Apply(v => v * 10, x); } } }");
        out = sprintf('%g', IxJ.Use.Tenfold(4));
    case 'delegates_events'
        % A compiled delegate type takes a function handle, and a compiled event takes a listener.
        jgraph.net.compile(["namespace IxK { public delegate double Op(double x); public static class Twice { public static double Of(Op f, double x) { return f(f(x)); } } }", ...
            "namespace IxK { public class Bell { public int Heard; public event System.EventHandler Rang; public void Ring() { Rang?.Invoke(this, System.EventArgs.Empty); } public void Hear() { Heard++; } } }"]);
        b = IxK.Bell();
        lh = addlistener(b, 'Rang', @(s, e) s.Hear()); %#ok<NASGU>
        b.Ring();
        b.Ring();
        out = sprintf('%g %d %s', IxK.Twice.Of(@(x) x * 3, 2), b.Heard, class(b));
    case 'unsafe'
        % unsafe code needs AllowUnsafe=true.
        src = "namespace IxL { public static class P { public static unsafe int Deref() { int v = 5; int* q = &v; return *q; } } }";
        try
            jgraph.net.compile(src);
            first = 'none';
        catch e
            first = e.identifier;
        end
        jgraph.net.compile(src, AllowUnsafe=true);
        out = sprintf('%s %d', first, IxL.P.Deref());
    case 'bad_option'
        try
            jgraph.net.compile("namespace IxM { }", Colour="red");
            out = 'none';
        catch e
            out = e.identifier;
        end
    case 'bad_language'
        try
            jgraph.net.compile("namespace IxN { }", LanguageVersion="99");
            out = 'none';
        catch e
            out = e.identifier;
        end
    case 'missing_file'
        try
            jgraph.net.compile("NoSuchHelper.cs");
            out = 'none';
        catch e
            out = e.identifier;
        end
    case 'which'
        out = which('jgraph.net.compile');
    otherwise
        error('ix_compile: no case %s', kind);
end
end
