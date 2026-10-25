function [y, info] = k8echo(x)
%K8ECHO Return the argument and a line saying what it arrived as.
y = x;
info = class(x);
if ~isreal(x)
    info = [info ' complex'];
end
if issparse(x)
    info = [info ' sparse'];
end
info = [info ' ' mat2str(size(x))];
end
