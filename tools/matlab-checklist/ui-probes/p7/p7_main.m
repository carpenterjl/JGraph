% p7_main - the acceptance set's top-level script (packages and class folders plan, stage P7): it
% imports the library's package and uses the class folder as a user would. The variable p7file
% names the trace file; p7_accept sets it before running this script. One key<TAB>value line each.
import signal.Trace
import signal.io.*

t = readTrace(p7file);                       % the wildcard import's member
fprintf('name\t%s\n', t.Name);
fprintf('data\t%s\n', mat2str(t.Data));
fprintf('peak\t%g\n', t.Peak);
scale(t, 2);                                 % a handle: this workspace's object changes
fprintf('scaled\t%s\n', mat2str(t.Data));
u = Trace('synthetic', 1:4);                 % the explicit import's class
fprintf('class\t%s %s\n', class(u), class(t));
fprintf('fromfile\t%s\n', signal.Trace.fromFile(p7file).Name);
signal.summarize                             % the script in the package, run in this workspace
s = Series.ofTrace(t);                       % the class folder's static
fprintf('values\t%s\n', mat2str(s.Values));
fprintf('total\t%g\n', total(s));            % a declared method file, through private/weight.m
fprintf('smooth\t%s\n', mat2str(smooth(s, 3), 4));
fprintf('describe\t%s | %s\n', describe(s), describe(t));   % the undeclared method file, and the handle class's
fprintf('isa\t%d %d %d %d\n', isa(t, 'signal.Trace'), isa(t, 'handle'), isa(s, 'Series'), isa(u, 'Trace'));
h = @signal.io.readTrace;
fprintf('handle\t%s %s\n', func2str(h), class(h(p7file)));
