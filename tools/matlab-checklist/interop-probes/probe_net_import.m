% probe_net_import: import for .NET names. Parsing a file that contains import System.* loads the
% default runtime (framework) before its first line runs, so the runtime is chosen here and the
% body, which holds the imports, is parsed only when it is called.
dotnetenv("core", Version="8");
ip_px('dotnetenv.status', 'disp(dotnetenv().Status)');
ip_import_body
