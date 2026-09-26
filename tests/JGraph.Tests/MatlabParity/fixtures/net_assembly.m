% net_assembly.m -- loading assemblies and the runtime (interop plan, stage 3): dotnetenv before
% and after the runtime loads, NET.addAssembly by path, by name and by full name, its refusals, the
% NET.Assembly it returns, and the process state .NET and MATLAB share (folder, environment).

ix_chk('env_before_runtime', char(dotnetenv().Runtime));
ix_chk('env_before_status', char(dotnetenv().Status));
ix_chk('env_bad_runtime', ix_id(@() dotnetenv("nope")));
dotnetenv("core", Version="8");
ix_chk('env_core_status', char(dotnetenv().Status));
ix_chk('env_core_again', ix_id(@() dotnetenv("core", Version="8")));
p = interop_paths();

a = NET.addAssembly(p.assembly);
ix_chk('env_after_load', char(dotnetenv().Status));
ix_chk('env_switch_after_load', ix_id(@() dotnetenv("framework")));
ix_chk('env_switch_after_load_message', ix_msg(@() dotnetenv("framework")));
ix_chk('runtime_version_major', System.Environment.Version.Major);
ix_chk('assembly_class', class(a));
ix_chk('assembly_isa_handle', isa(a, 'handle'));
ix_chk('AssemblyHandle', class(a.AssemblyHandle));
ix_chk('Classes_count', numel(a.Classes));
ix_chk('Classes_first', a.Classes{1});
ix_chk('Enums', a.Enums);
ix_chk('Structures', a.Structures);
ix_chk('Delegates', a.Delegates);
ix_chk('GenericTypes', a.GenericTypes);
ix_chk('Interfaces', a.Interfaces);
ix_chk('properties', strjoin(properties(a)', ','));
ix_chk('reload_same', NET.addAssembly(p.assembly) == a);
ix_chk('path_string', class(NET.addAssembly(string(p.assembly))));
ix_chk('name_System_Xml', class(NET.addAssembly('System.Xml')));
ix_chk('name_System_Xml_type', class(System.Xml.XmlDocument()));
ix_chk('name_full', class(NET.addAssembly('System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a')));
ix_chk('name_CoreLib', class(NET.addAssembly('System.Private.CoreLib')));
ix_chk('name_nosuch', ix_id(@() NET.addAssembly('No.Such.Assembly')));
ix_chk('name_nosuch_message', ix_msg(@() NET.addAssembly('No.Such.Assembly')));
ix_chk('path_missing', ix_id(@() NET.addAssembly('C:\no\such\file.dll')));
ix_chk('path_relative_is_a_name', ix_id(@() NET.addAssembly('JGraph.Interop.TestAssembly.dll')));
ix_chk('native_dll', ix_id(@() NET.addAssembly(p.lib)));
ix_chk('native_dll_message', ix_msg(@() NET.addAssembly(p.lib)));
ix_chk('not_a_dll', ix_msg(@() NET.addAssembly(p.header)));
ix_chk('numeric', ix_id(@() NET.addAssembly(5)));
ix_chk('numeric_message', ix_msg(@() NET.addAssembly(5)));
ix_chk('no_args', ix_id(@() NET.addAssembly()));
ix_chk('two_args', ix_id(@() NET.addAssembly(p.assembly, 1)));
ix_chk('isNETSupported', NET.isNETSupported);
ix_chk('which_addAssembly', which('NET.addAssembly'));
ix_chk('exist_NetException', exist('NET.NetException', 'class'));
ix_chk('exist_Assembly', exist('NET.Assembly', 'class'));
ix_chk('exist_addAssembly', exist('NET.addAssembly'));
ix_chk('disableAutoRelease_not_com', ix_id(@() NET.disableAutoRelease(JGTest.Members())));

% ---- process state both sides share
ix_chk('cwd_shared', strcmp(char(System.Environment.CurrentDirectory), pwd));
here = pwd;
cd(tempdir);
ix_chk('cwd_follows_cd', strcmp(char(System.Environment.CurrentDirectory), pwd));
cd(here);
setenv('JG_FIXTURE_VAR', 'abc');
ix_chk('env_setenv_seen', System.Environment.GetEnvironmentVariable('JG_FIXTURE_VAR'));
System.Environment.SetEnvironmentVariable('JG_FIXTURE_VAR2', 'xyz');
ix_chk('env_net_set_seen', getenv('JG_FIXTURE_VAR2'));
ix_chk('Is64BitProcess', System.Environment.Is64BitProcess);
