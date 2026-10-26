function s = k15env(key)
%K15ENV What a deployed function sees of its server: the deployment builtins and the user data.
root = ctfroot;
[parent, leaf] = fileparts(root);
[~, parentLeaf] = fileparts(parent);
s = struct();
s.isdeployed = isdeployed;
s.ismcc = ismcc;
s.ctfroot_leaf = leaf;
s.ctfroot_parent = parentLeaf;
s.ctfroot_exists = exist(root, 'dir') == 7;
s.mcrversion = mcrversion;
s.pwd_leaf = pwdLeaf();
s.userdata = describe(getmcruserdata(key));
s.userdata_missing = describe(getmcruserdata('k15nosuch'));
s.exist_web = exist('k15web');
s.exist_k14add = exist('k14add');
s.which_web = which('k15web');
s.which_web = strrep(s.which_web, root, '<ctfroot>');
s.nargin_add = nargin('k14add');
w = warning('query', 'MATLAB:dispatcher:nameConflict');
s.warn_state = w.state;
s.beep = beep;
[archive_name, in_mps] = archiveName(root);
s.archive_name = archive_name;
s.in_mps = in_mps;
end

function s = describe(v)
if ischar(v)
    s = ['char:' v];
elseif isempty(v)
    s = ['empty:' class(v)];
else
    s = class(v);
end
end

function leaf = pwdLeaf()
[~, leaf] = fileparts(pwd);
end

function [name, in_mps] = archiveName(root)
% What prodserver.metrics names the archive: the ctfroot folder's name.
[~, name, ~] = fileparts(root);
in_mps = ~isempty(getenv('MPS_ENDPOINT_ROOT')) || ~isempty(getenv('MPS_INSTANCE_ROOT'));
end
