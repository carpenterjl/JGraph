function varargout = k17_varout(n)
%K17_VAROUT Answers as many outputs as were asked for, the k-th being k times n (stage K17).
for k = 1:max(nargout, 1)
    varargout{k} = k * n; %#ok<AGROW>
end
end
