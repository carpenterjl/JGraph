function acc_pkgtool(varargin)
%ACC_PKGTOOL The acceptance set's console program over a +package and an @Class folder (packages
%and class folders plan, stage P6): the package function acc.fold, the class AccSeries defined by
%its folder with a method file and a private function, a handle to the package function, and
%which over both in the deployed session. One key<TAB>value line each.
%
%   acc_pkgtool <factor>   folds the data scaled by the factor
k0_quiet;
fprintf('nargin\t%d\n', nargin);
fprintf('isdeployed\t%d\n', isdeployed);
v = [3 1 4 1 5];
if nargin >= 1
    v = v * str2double(varargin{1});
end
fprintf('fold\t%s\n', mat2str(acc.fold(v)));
s = AccSeries(v);
fprintf('class\t%s\n', class(s));
fprintf('total\t%g\n', s.total());
fprintf('total2\t%g\n', total(AccSeries([1 2])));
h = @acc.fold;
fprintf('handle\t%s %s\n', func2str(h), mat2str(h([1 1])));
fprintf('exist\t%d %d\n', exist('AccSeries', 'class'), exist('acc.fold'));
w = which('acc.fold');
fprintf('which\t%s\n', regexprep(w, '^.*[\\/]', ''));
fprintf('end\n');
end
