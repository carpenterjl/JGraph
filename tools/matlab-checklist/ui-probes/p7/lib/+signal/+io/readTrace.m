function t = readTrace(file)
%READTRACE Reads a trace file - a name on its first line, one sample per line after it - into a
%signal.Trace. A member of the sub-package signal.io; it names the package's class in full, as a
%package member must, and its numbers come from the sub-package's own private function (a
%private/ folder serves the files beside it alone: one under +signal would not be seen from here,
%measured in both engines).
lines = regexp(strtrim(fileread(file)), '\r?\n', 'split');
t = signal.Trace(strtrim(lines{1}), parseNumbers(lines(2:end)));
end
