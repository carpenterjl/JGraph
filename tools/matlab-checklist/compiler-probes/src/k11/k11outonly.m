function [a, varargout] = k11outonly()
%K11OUTONLY Outputs only: nargout, then 10, 20, ... for each further output.
a = nargout;
for k = 1:nargout - 1
    varargout{k} = 10 * k; %#ok<AGROW>
end
end
