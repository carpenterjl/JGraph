function varargout = k10echo(varargin)
%K10ECHO Return the arguments as they arrived, for the client to read back.
varargout = varargin(1:max(nargout, 1));
end
