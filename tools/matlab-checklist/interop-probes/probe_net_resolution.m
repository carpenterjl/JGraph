% probe_net_resolution: where .NET names sit in MATLAB's name resolution, and the query verbs.
dotnetenv("core", Version="8");
a = ip_assets();
NET.addAssembly(a.assembly);
root = tempname; mkdir(root);
w = @(rel, text) ip_write(fullfile(root, rel), text);

% ---- query verbs on .NET names
ip_pr('which.String', 'which(''System.String'')');
ip_px('which.String.disp', 'which System.String');
ip_pr('which.Members', 'which(''JGTest.Members'')');
ip_pr('which.method', 'which(''System.Math.Max'')');
ip_pr('exist.String', 'exist(''System.String'')');
ip_pr('exist.String.class', 'exist(''System.String'', ''class'')');
ip_pr('exist.System', 'exist(''System'')');
ip_pr('exist.Members', 'exist(''JGTest.Members'')');
ip_pr('exist.nosuch', 'exist(''JGTest.NoSuch'')');
ip_pr('metaclass.q', 'class(?System.String)');
ip_pr('metaclass.fromName', 'class(meta.class.fromName(''System.String''))');
ip_pr('metaclass.Name', 'meta.class.fromName(''JGTest.Members'').Name');
ip_pr('metaclass.Superclasses', 'numel(meta.class.fromName(''JGTest.Members'').SuperclassList)');
ip_pr('metapackage.System', 'class(meta.package.fromName(''System''))');
ip_pr('namespace.alone', 'System');
ip_pr('namespace.partial', 'System.Collections');
ip_pr('namespace.class', 'class(System.Collections)');
ip_pr('func2str', 'func2str(@System.Math.Max)');
ip_pr('handle.static', 'feval(@System.Math.Max, 3, 4)');
ip_pr('handle.ctor', 'class(feval(@System.String, ''x''))');
ip_pr('feval.string', 'feval(''System.Math.Max'', 3, 4)');
ip_pr('str2func', 'feval(str2func(''System.Math.Max''), 3, 4)');
ip_pr('help.nonempty', 'numel(help(''System.String'')) > 0');
ip_pr('doc.exists', 'exist(''doc'')');

% ---- a variable called System or JGTest
System = 5; %#ok<NASGU>
ip_pr('var.System.call', 'System.String(''x'')');
ip_pr('var.System.Math', 'System.Math.Max(1, 2)');
clear System
ip_pr('var.cleared', 'class(System.String(''x''))');
s.Members = 7;
JGTest = s; %#ok<NASGU>
ip_pr('var.JGTest.field', 'JGTest.Members');
clear JGTest s

% ---- a function file with the namespace's name
w('f1/System.m', 'function r = System(varargin), r = ''System.m function''; end');
addpath(fullfile(root, 'f1')); rehash;
ip_pr('file.System.bare', 'System');
ip_pr('file.System.dotted', 'class(System.String(''x''))');
ip_pr('file.System.Math', 'System.Math.Max(1, 2)');
rmpath(fullfile(root, 'f1')); rehash;

% ---- a function file with the root namespace of a user assembly
w('f2/JGTest.m', 'function r = JGTest(varargin), r = ''JGTest.m function''; end');
addpath(fullfile(root, 'f2')); rehash;
ip_pr('file.JGTest.bare', 'JGTest');
ip_pr('file.JGTest.dotted', 'class(JGTest.Members())');
rmpath(fullfile(root, 'f2')); rehash;

% ---- a MATLAB package with the same name as a .NET namespace
w('f3/+JGTest/Members.m', 'function r = Members(varargin), r = ''package function''; end');
w('f3/+JGTest/OnlyInPackage.m', 'function r = OnlyInPackage(varargin), r = ''package only''; end');
addpath(fullfile(root, 'f3')); rehash;
ip_pr('pkg.JGTest.Members', 'JGTest.Members()');
ip_pr('pkg.JGTest.OnlyInPackage', 'JGTest.OnlyInPackage()');
ip_pr('pkg.JGTest.Statics', 'JGTest.Statics.Hello()');
rmpath(fullfile(root, 'f3')); rehash;

% ---- a classdef called System in a package-less folder, and NET.m
w('f4/NET.m', 'function r = NET(varargin), r = ''NET.m function''; end');
addpath(fullfile(root, 'f4')); rehash;
ip_pr('file.NET.bare', 'NET');
ip_pr('file.NET.isNETSupported', 'NET.isNETSupported');
rmpath(fullfile(root, 'f4')); rehash;

% ---- collisions between .NET types and MATLAB functions
ip_pr('coll.Sum.Of', 'JGTest.Sum.Of([1 2 3])');
ip_pr('coll.plot', 'JGTest.plot().Kind');
ip_pr('coll.max.Thing', 'JGTest.max.Thing().Where()');
ip_pr('coll.max.fn', 'max([1 5 2])');
ip_pr('coll.sum.fn', 'sum([1 2 3])');

% ---- nested and generic type names written directly
ip_pr('direct.nested.plus', 'class(JGTest.Outer.MakeInner())');
ip_pr('direct.generic.name', 'System.Collections.Generic.List');
ip_pr('direct.generic.static', 'System.Collections.Generic.Comparer');

% ---- a bare static method with no import
ip_pr('bare.Max', 'Max(1, 2)');

% ---- in a function file: does a .NET name resolve the same way inside a function?
w('f5/ip_fn_uses_net.m', sprintf('function r = ip_fn_uses_net()\nr = System.Math.Max(3, 9);\nend'));
w('f5/ip_fn_local_var.m', sprintf('function r = ip_fn_local_var()\nSystem = 1; %%#ok<NASGU>\nr = System.Math.Max(3, 9);\nend'));
addpath(fullfile(root, 'f5')); rehash;
ip_pr('fn.uses.net', 'ip_fn_uses_net()');
ip_pr('fn.local.var', 'ip_fn_local_var()');
rmpath(fullfile(root, 'f5')); rehash;

rmdir(root, 's');
