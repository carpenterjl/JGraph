function varargout = k8vout(varargin)
%K8VOUT Answer as many outputs as asked for: output k is k, the last says nargin and nargout.
for k = 1:nargout
    varargout{k} = k;
end
if nargout > 0
    varargout{nargout} = sprintf('nargin=%d nargout=%d', nargin, nargout);
end
end
