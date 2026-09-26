% probe_net_assembly: NET.addAssembly forms and failures, dotnetenv, and the rest of NET.*.
% NET.isNETSupported loads the default runtime (framework), so it comes after dotnetenv here;
% probe_net_isnet records the order the other way round.
ip_px('dotnetenv.disp.before', 'dotnetenv');
ip_px('dotnetenv.bad.runtime', 'dotnetenv("nope")');
ip_px('dotnetenv.core', 'dotnetenv("core", Version="8")');
ip_px('dotnetenv.again.same', 'dotnetenv("core", Version="8")');
ip_px('dotnetenv.core.noversion', 'dotnetenv("core")');
ip_pr('isNETSupported.first', 'NET.isNETSupported');
a = ip_assets();

ip_pr('add.path', 'class(NET.addAssembly(a.assembly))');
ip_px('dotnetenv.after.load', 'dotnetenv');
ip_px('dotnetenv.switch.after.load', 'dotnetenv("framework")');
ip_pr('add.path.again', 'NET.addAssembly(a.assembly).Classes{1}');
ip_pr('add.path.string', 'class(NET.addAssembly(string(a.assembly)))');
ip_pr('add.name.System.Xml', 'class(NET.addAssembly(''System.Xml''))');
ip_pr('add.name.System.Xml.class', 'class(System.Xml.XmlDocument())');
ip_pr('add.name.core', 'NET.addAssembly(''System.Private.CoreLib'').Classes{1}');
ip_pr('add.name.full', 'class(NET.addAssembly(''System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a''))');
ip_pr('add.name.forms', 'class(NET.addAssembly(''System.Windows.Forms''))');
ip_pr('add.name.nosuch', 'NET.addAssembly(''No.Such.Assembly'')');
ip_pr('add.path.missing', 'NET.addAssembly(''C:\no\such\file.dll'')');
ip_pr('add.path.relative', 'NET.addAssembly(''JGraph.Interop.TestAssembly.dll'')');
ip_pr('add.native.dll', 'NET.addAssembly(a.lib)');
ip_pr('add.not.dll', 'NET.addAssembly(a.header)');
ip_pr('add.numeric', 'NET.addAssembly(5)');
ip_pr('add.noargs', 'NET.addAssembly()');
ip_pr('add.twoargs', 'NET.addAssembly(a.assembly, 1)');
ip_pr('add.reflection', 'class(NET.addAssembly(System.Reflection.Assembly.GetExecutingAssembly()))');
ip_pr('unload.exists', 'exist(''NET.unloadAssembly'')');
ip_pr('isNETSupported.after', 'NET.isNETSupported');

% ---- the rest of the NET namespace
ip_pr('NET.methods', 'methods(''NET'')');
ip_pr('NET.what', 'what(''NET'')');
ip_pr('NET.help', 'numel(help(''NET'')) > 0');
m = JGTest.Members();
ip_px('disableAutoRelease', 'NET.disableAutoRelease(m)');
ip_px('enableAutoRelease', 'NET.enableAutoRelease(m)');
ip_px('setStaticProperty', 'NET.setStaticProperty(''JGTest.Members.StaticField'', 8)');
ip_pr('setStaticProperty.after', 'JGTest.Members.StaticField');
ip_px('setStaticProperty.prop', 'NET.setStaticProperty(''JGTest.Members.Counter'', int32(9))');
ip_pr('setStaticProperty.prop.after', 'JGTest.Members.Counter');
ip_px('setStaticProperty.readonly', 'NET.setStaticProperty(''JGTest.Members.StaticReadOnly'', ''x'')');
ip_px('setStaticProperty.nosuch', 'NET.setStaticProperty(''JGTest.Members.NoSuch'', 1)');
ip_pr('NetException.class', 'exist(''NET.NetException'', ''class'')');
ip_pr('Assembly.class', 'exist(''NET.Assembly'', ''class'')');
ip_pr('GenericClass.class', 'exist(''NET.GenericClass'', ''class'')');
ip_pr('explicitCast.exists', 'exist(''NET.explicitCast'')');
ip_pr('invokeGenericMethod.exists', 'exist(''NET.invokeGenericMethod'')');
ip_pr('createGeneric.exists', 'exist(''NET.createGeneric'')');
ip_pr('convertArray.exists', 'exist(''NET.convertArray'')');
ip_pr('createArray.exists', 'exist(''NET.createArray'')');
ip_pr('addAssembly.exists', 'exist(''NET.addAssembly'')');
ip_pr('which.addAssembly', 'which(''NET.addAssembly'')');

% ---- environment of the loaded runtime
ip_pr('env.Version', 'char(System.Environment.Version.ToString())');
ip_pr('env.CurrentDirectory', 'strcmp(char(System.Environment.CurrentDirectory), pwd)');
here = pwd; addpath(here); cd(tempdir);
ip_pr('env.CurrentDirectory.after.cd', 'strcmp(char(System.Environment.CurrentDirectory), pwd)');
setenv('JG_PROBE_VAR', 'abc');
ip_pr('env.GetEnvironmentVariable', 'System.Environment.GetEnvironmentVariable(''JG_PROBE_VAR'')');
System.Environment.SetEnvironmentVariable('JG_PROBE_VAR2', 'xyz');
ip_pr('env.getenv.after.net.set', 'getenv(''JG_PROBE_VAR2'')');
ip_pr('env.Is64Bit', 'System.Environment.Is64BitProcess');
ip_pr('env.RuntimeDirectory', 'char(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory())');
cd(here);
