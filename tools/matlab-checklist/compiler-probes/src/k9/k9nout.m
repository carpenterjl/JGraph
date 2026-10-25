function varargout = k9nout(n)
%K9NOUT As many outputs as asked for, each its position times n.
for k = 1:nargout
    varargout{k} = k * n;
end
end
