function k7_hello(varargin)
%K7_HELLO Stage K7's main (compiler and SDK plan): a console program that prints its arguments.
fprintf('nargin\t%d\n', nargin);
for k = 1:nargin
    fprintf('arg%d\t%s\n', k, varargin{k});
end
end
