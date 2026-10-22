function c = addtwo(a, b)
%ADDTWO Return a+b; char arguments are converted with str2double when deployed.
if isdeployed
    if ischar(a) || isstring(a), a = str2double(a); end
    if ischar(b) || isstring(b), b = str2double(b); end
end
c = a + b;
fprintf('addtwo: class(a)=%s class(b)=%s size(a)=%s\n', class(a), class(b), mat2str(size(a)));
disp(c);
end
