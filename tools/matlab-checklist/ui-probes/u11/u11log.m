function u11log(text)
% U11LOG Appends a line to the probes' shared log.
global U11LOG %#ok<GVMIS>
U11LOG{end + 1} = text;
end
