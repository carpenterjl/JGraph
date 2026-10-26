% record: -noFigureWindows
% Compiler and SDK plan, stage K15: MATLAB Production Server's in-MATLAB helpers - the persistence
% cache over the MatlabTest (MAT-file) provider (mps.cache.control, connect, DataCache's methods and
% dot access, Connection's list), mps.sync.mutex over it, and prodserver.metrics' events and
% refusals. Nothing here needs a server; the service's folder is under the temp folder.
beep('off');
root = tempname;
mkdir(root);
folder = fullfile(root, 'svc');

% --- prodserver.metrics: the event line, and every refusal ----------------------------------------
chk('m_counter', @() evalc('prodserver.metrics.incrementCounter(''k15_calls_total'', 1)'));
chk('m_counter_string', @() evalc('prodserver.metrics.incrementCounter("k15_calls_total", 2.5)'));
chk('m_gauge', @() evalc('prodserver.metrics.setGauge(''k15_level'', 4)'));
chk('m_gauge_neg', @() evalc('prodserver.metrics.setGauge(''k15_level'', -1.25)'));
chk('m_gauge_int', @() evalc('prodserver.metrics.setGauge(''k15_level'', int8(7))'));
chk('m_gauge_single', @() evalc('prodserver.metrics.setGauge(''k15_level'', single(0.1))'));
chk('m_big', @() evalc('prodserver.metrics.incrementCounter(''k15_big'', 123456789)'));
chk('m_small', @() evalc('prodserver.metrics.setGauge(''k15_small'', 0.000012345)'));
chk('m_nan', @() evalc('prodserver.metrics.setGauge(''k15_nan'', NaN)'));
chk('m_inf', @() evalc('prodserver.metrics.incrementCounter(''k15_inf'', Inf)'));
chk('m_zero_gauge', @() evalc('prodserver.metrics.setGauge(''k15_zero'', 0)'));
u2_chk('m_badname', @() run0(@() prodserver.metrics.incrementCounter('1bad', 1)));
u2_chk('m_dash', @() run0(@() prodserver.metrics.incrementCounter('k15-dash', 1)));
u2_chk('m_emptyname', @() run0(@() prodserver.metrics.incrementCounter('', 1)));
u2_chk('m_cellname', @() run0(@() prodserver.metrics.incrementCounter({'k15'}, 1)));
u2_chk('m_strings', @() run0(@() prodserver.metrics.incrementCounter(["a" "b"], 1)));
u2_chk('m_number_name', @() run0(@() prodserver.metrics.incrementCounter(5, 1)));
u2_chk('m_zero', @() run0(@() prodserver.metrics.incrementCounter('k15_zero', 0)));
u2_chk('m_negative', @() run0(@() prodserver.metrics.incrementCounter('k15_neg', -1)));
u2_chk('m_textvalue', @() run0(@() prodserver.metrics.incrementCounter('k15_text', '1')));
u2_chk('m_vector', @() run0(@() prodserver.metrics.setGauge('k15_vec', [1 2])));
u2_chk('m_empty_value', @() run0(@() prodserver.metrics.setGauge('k15_vec', [])));
u2_chk('m_complex', @() run0(@() prodserver.metrics.setGauge('k15_cplx', 1 + 2i)));
u2_chk('m_logical', @() run0(@() prodserver.metrics.setGauge('k15_logical', true)));
u2_chk('m_logical_counter', @() run0(@() prodserver.metrics.incrementCounter('k15_logical', true)));
u2_chk('m_cell_value', @() run0(@() prodserver.metrics.setGauge('k15_cell', {1})));
u2_chk('m_noargs', @() run0(@() prodserver.metrics.incrementCounter()));
u2_chk('m_onearg', @() run0(@() prodserver.metrics.setGauge('k15_one')));
u2_chk('m_threeargs', @() run0(@() prodserver.metrics.setGauge('k15_three', 1, 2)));
u2_chk('m_output', @() prodserver.metrics.setGauge('k15_out', 1));
u2_chk('m_nargin', @() nargin('prodserver.metrics.incrementCounter'));
u2_chk('m_nargout', @() nargout('prodserver.metrics.setGauge'));

% --- the controller: a MatlabTest (MAT-file) service -------------------------------------------
u2_chk('ctrl_none', @() mps.cache.control());
u2_chk('ctrl_unknown_connection', @() mps.cache.control('NoSuch'));
u2_chk('ctrl_unknown_provider', @() mps.cache.control('k15x', 'NoSuchProvider'));
u2_chk('ctrl_number', @() mps.cache.control(5, 'MatlabTest'));
u2_chk('ctrl_empty_name', @() mps.cache.control('', 'MatlabTest'));
c = mps.cache.control('k15conn', 'MatlabTest', 'Folder', folder);
u2_chk('ctrl_class', @() class(c));
u2_chk('ctrl_connection_name', @() c.ConnectionName);
u2_chk('ctrl_provider', @() c.ProviderName);
u2_chk('ctrl_folder', @() strrep(c.Folder, folder, '<folder>'));
u2_chk('ctrl_operations', @() char(c.Operations));
u2_chk('ctrl_active', @() char(c.ActiveConnection));
u2_chk('ctrl_manage', @() char(c.ManageService));
u2_chk('ctrl_connection_class', @() c.ConnectionClass);
u2_chk('ctrl_ctrlparams', @() strrep(c.CtrlParams, folder, '<folder>'));
u2_chk('ctrl_props', @() strjoin(sort(properties(c))', ' ')); % R2025b lists a dynamicprops object's properties in an order that changes run to run
u2_chk('ctrl_methods', @() strjoin(sort(methods(c))', ' '));
u2_chk('ctrl_duplicate', @() mps.cache.control('k15conn', 'MatlabTest', 'Folder', folder));
u2_chk('ctrl_lookup', @() class(mps.cache.control('k15conn')));
u2_chk('ctrl_stop_before_start', @() run0(@() stop(c)));
u2_chk('ctrl_ping_before_start', @() ping(c));
u2_chk('ctrl_restart_before_start', @() run0(@() restart(c)));
u2_chk('ctrl_detach_before_start', @() run0(@() detach(c)));
u2_chk('ctrl_connect_before_start', @() mps.cache.connect('k15cache', 'Connection', 'k15conn'));
u2_chk('ctrl_start', @() run0(@() start(c)));
u2_chk('ctrl_folder_exists', @() exist(folder, 'dir'));
u2_chk('ctrl_active_after', @() char(c.ActiveConnection));
u2_chk('ctrl_manage_after', @() char(c.ManageService));
u2_chk('ctrl_start_again', @() warned(@() start(c)));
u2_chk('ctrl_ping', @() ping(c));
chk('ctrl_ping_shown', @() strrep(shownPing(c), folder, '<folder>'));
u2_chk('ctrl_version', @() ischar(version(c)) && ~isempty(version(c)));
u2_chk('ctrl_attach_after_start', @() run0(@() attach(c)));
u2_chk('ctrl_detach_after_start', @() run0(@() detach(c)));
u2_chk('ctrl_list', @() strjoin(mps.cache.Connection.list(), ' '));
u2_chk('ctrl_exist', @() mps.cache.Connection.exist('k15conn'));
u2_chk('ctrl_exist_no', @() mps.cache.Connection.exist('nosuch'));

% --- the cache ---------------------------------------------------------------------------------
u2_chk('cache_bad_name', @() mps.cache.connect('1bad', 'Connection', 'k15conn'));
u2_chk('cache_unknown_connection', @() mps.cache.connect('k15cache', 'Connection', 'nosuch'));
u2_chk('cache_bad_option', @() mps.cache.connect('k15cache', 'Connection', 'k15conn', 'Bogus', 1));
k = mps.cache.connect('k15cache', 'Connection', 'k15conn');
u2_chk('cache_class', @() class(k));
u2_chk('cache_default_connection', @() class(mps.cache.connect('k15other')));
u2_chk('cache_getp_name', @() getp(k, 'Name'));
u2_chk('cache_getp_connection', @() getp(k, 'Connection'));
u2_chk('cache_getp_folder', @() strrep(getp(k, 'Folder'), folder, '<folder>'));
u2_chk('cache_getp_operations', @() char(getp(k, 'Operations')));
u2_chk('cache_getp_localkeys', @() class(getp(k, 'LocalKeys')));
u2_chk('cache_getp_bad', @() getp(k, 'Nosuch'));
u2_chk('cache_length_0', @() length(k));
u2_chk('cache_length_class', @() class(length(k)));
u2_chk('cache_keys_0', @() size(keys(k)));
u2_chk('cache_keys_0_class', @() class(keys(k)));
u2_chk('cache_get_missing', @() class(k.nosuch));
u2_chk('cache_get_missing_size', @() size(k.nosuch));
k.temperature = [350 107 28];
u2_chk('cache_dot_set_get', @() k.temperature);
u2_chk('cache_length_1', @() length(k));
u2_chk('cache_keys_1', @() keys(k));
put(k, 'numbers', [3 5 7 9], 'names', {'three', 'five', 'seven', 'nine'});
u2_chk('cache_put_pairs', @() strjoin(keys(k)', ' '));
u2_chk('cache_get_two', @() get(k, 'temperature', 'numbers'));
u2_chk('cache_get_cell', @() get(k, {'numbers', 'names'}));
u2_chk('cache_get_string', @() get(k, ["names", "numbers"]));
u2_chk('cache_get_one', @() get(k, 'names'));
u2_chk('cache_get_two_out', @() twoOut(@() get(k, 'temperature', 'numbers')));
u2_chk('cache_iskey', @() isKey(k, 'numbers', 'nosuch', 'names'));
u2_chk('cache_iskey_cell', @() isKey(k, {'numbers', 'nosuch'}));
u2_chk('cache_iskey_string', @() isKey(k, "names"));
put(k, {'Vb', 'Vbe', 'Rb'}, {12, 0.5, 500000});
u2_chk('cache_put_cells', @() strjoin(keys(k)', ' '));
put(k, ["Sa", "Sb"], {1, 2});
u2_chk('cache_put_strings', @() strjoin(keys(k)', ' '));
u2_chk('cache_put_count_mismatch', @() run0(@() put(k, {'a', 'b'}, {1})));
u2_chk('cache_put_odd', @() run0(@() put(k, 'a')));
u2_chk('cache_put_cell_value', @() putGet(k, 'V', {2, 4, 8}));
u2_chk('cache_put_struct_value', @() putGet(k, 'S', struct('a', 1, 'b', 'x')));
u2_chk('cache_put_string_value', @() putGet(k, 'T', "text"));
u2_chk('cache_put_empty_value', @() putGet(k, 'E', []));
u2_chk('cache_put_logical', @() putGet(k, 'L', true));
u2_chk('cache_put_int', @() putGet(k, 'I', int32(-5)));
u2_chk('cache_put_matrix', @() putGet(k, 'M', magic(3)));
u2_chk('cache_overwrite', @() putGet(k, 'temperature', 1));
u2_chk('cache_bytes', @() bytes(k, 'numbers'));
u2_chk('cache_bytes_missing', @() bytes(k, 'nosuch'));
u2_chk('cache_bytes_two', @() bytes(k, {'Vb', 'nosuch', 'names'}));
u2_chk('cache_length_many', @() length(k));
u2_chk('cache_length_remote', @() length(k, mps.cache.Location.Remote));
u2_chk('cache_length_local', @() length(k, mps.cache.Location.Local));
u2_chk('cache_remove_one', @() remove(k, 'Vb'));
u2_chk('cache_remove_class', @() class(remove(k, 'Vbe')));
u2_chk('cache_remove_missing', @() remove(k, 'nosuch'));
u2_chk('cache_remove_many', @() remove(k, {'Rb', 'Sa', 'nosuch'}));
u2_chk('cache_remove_string', @() remove(k, "Sb"));
u2_chk('cache_keys_after', @() strjoin(keys(k)', ' '));
u2_chk('cache_count_remote', @() countRemoteKeys(k));
u2_chk('cache_bad_key_get', @() get(k, 5));
u2_chk('cache_bad_key_put', @() run0(@() put(k, 5, 1)));
u2_chk('cache_bad_dot_set', @() run0(@() setBadKey(k)));
u2_chk('cache_shadow_warning', @() warned(@() put(k, 'keys', 1)));
u2_chk('cache_shadow_get', @() k.keys);
u2_chk('cache_remove_shadow', @() remove(k, 'keys'));

% composite keys
key1 = struct('time', 17, 'id', 23);
key2 = struct('time', 18, 'id', 23);
put(k, key1, {'data', 'later'});
u2_chk('cache_composite_get', @() get(k, key1));
chk('cache_composite_dot', @() oneLine(@() k.(key1)));
u2_chk('cache_composite_iskey', @() isKey(k, key1, key2));
u2_chk('cache_composite_keys', @() compositeKeys(keys(k)));
u2_chk('cache_composite_remove', @() remove(k, key1));
u2_chk('cache_composite_gone', @() compositeKeys(keys(k)));

% local keys
u2_chk('cache_retain_get', @() size(retain(k)));
retain(k, 'phi', 'a');
u2_chk('cache_retain_set', @() strjoin(retain(k), ' '));
u2_chk('cache_retain_local_keys', @() strjoin(keys(k, mps.cache.Location.Local)', ' '));
k.phi = 1.61803;
k.a = 17;
u2_chk('cache_local_get', @() k.phi);
u2_chk('cache_local_length', @() length(k));
u2_chk('cache_local_length_local', @() length(k, mps.cache.Location.Local));
u2_chk('cache_local_length_remote', @() length(k, mps.cache.Location.Remote));
u2_chk('cache_local_remote_keys', @() ismember('phi', keys(k, mps.cache.Location.Remote)));
u2_chk('cache_flush', @() strjoin(sort(flush(k))', ' '));
u2_chk('cache_flush_remote', @() ismember('phi', keys(k, mps.cache.Location.Remote)));
u2_chk('cache_flush_again', @() size(flush(k)));
k.a = 18;
u2_chk('cache_purge', @() strjoin(purge(k)', ' '));
u2_chk('cache_purge_retain', @() size(retain(k)));
u2_chk('cache_purge_value', @() k.a);
retain(k, true);
u2_chk('cache_retain_true', @() retain(k));
k.b = 2;
u2_chk('cache_retain_true_local', @() length(k, mps.cache.Location.Local));
u2_chk('cache_retain_true_purge', @() strjoin(purge(k)', ' '));
retain(k, false);
u2_chk('cache_retain_false', @() retain(k));
u2_chk('cache_clear', @() clear(k));
u2_chk('cache_clear_again', @() clear(k));
u2_chk('cache_length_cleared', @() length(k));

% display
u2_chk('cache_disp', @() shown(k));
u2_chk('ctrl_disp', @() strrep(shownSorted(c), folder, '<folder>'));
chk('location_members', @() oneLine(@() strjoin(cellstr(string(enumeration('mps.cache.Location')))', ' ')));
u2_chk('tristate_true', @() double(mps.cache.TriStateBoolean.True));
u2_chk('tristate_unknown', @() double(mps.cache.TriStateBoolean.Unknown));

% a second cache on the same service
k2 = mps.cache.connect('k15second', 'Connection', 'k15conn');
k2.x = 1;
u2_chk('cache_second_keys', @() keys(k2));
u2_chk('cache_first_keys', @() size(keys(k)));
u2_chk('cache_files', @() strjoin(sort(cellstr(ls(fullfile(folder, '*.mat'))))', ' '));

% --- the mutex ---------------------------------------------------------------------------------
u2_chk('mutex_no_name', @() mps.sync.mutex());
u2_chk('mutex_bad_name', @() mps.sync.mutex('1bad', 'Connection', 'k15conn'));
u2_chk('mutex_unknown_connection', @() mps.sync.mutex('k15lock', 'Connection', 'nosuch'));
u2_chk('mutex_bad_expiration', @() mps.sync.mutex('k15lock', 'Connection', 'k15conn', 'Expiration', 1.5));
m = mps.sync.mutex('k15lock', 'Connection', 'k15conn');
u2_chk('mutex_class', @() class(m));
u2_chk('mutex_name', @() m.MutexName);
u2_chk('mutex_connection', @() m.ConnectionName);
u2_chk('mutex_expiration', @() m.Expiration);
u2_chk('mutex_props', @() strjoin(properties(m)', ' '));
u2_chk('mutex_methods', @() strjoin(sort(methods(m))', ' '));
u2_chk('mutex_own_before', @() own(m));
u2_chk('mutex_release_before', @() release(m));
u2_chk('mutex_acquire', @() acquire(m));
u2_chk('mutex_own', @() own(m));
u2_chk('mutex_acquire_again', @() acquire(m));
u2_chk('mutex_locks_dir', @() exist(fullfile(folder, 'locks'), 'dir'));
m2 = mps.sync.mutex('k15lock', 'Connection', 'k15conn', 'Expiration', 1);
chk('mutex_second_acquire', @() acquire(m2));
chk('mutex_second_own', @() own(m2));
u2_chk('mutex_release', @() release(m));
u2_chk('mutex_release_again', @() release(m));
u2_chk('mutex_own_after', @() own(m));
u2_chk('mutex_acquire_timeout', @() acquire(m, 0.1));
u2_chk('mutex_expire', @() expired(m2));
u2_chk('mutex_default_connection', @() class(mps.sync.mutex('k15lock2')));

% --- stopping the service --------------------------------------------------------------------------
u2_chk('ctrl_stop', @() run0(@() stop(c)));
u2_chk('ctrl_folder_gone', @() exist(folder, 'dir'));
u2_chk('ctrl_active_stopped', @() char(c.ActiveConnection));
u2_chk('cache_after_stop', @() keys(k));
u2_chk('ctrl_stop_again', @() run0(@() stop(c)));
u2_chk('ctrl_list_after', @() strjoin(mps.cache.Connection.list(), ' '));
mps.cache.Connection.clear('k15conn');
u2_chk('ctrl_list_cleared', @() strjoin(mps.cache.Connection.list(), ' '));
u2_chk('ctrl_lookup_cleared', @() mps.cache.control('k15conn'));
u2_chk('ctrl_keepdata', @() mps.cache.control('k15keep', 'MatlabTest', 'Folder', fullfile(root, 'keep'), 'KeepData', true));
d = mps.cache.control('k15second', 'MatlabTest', 'Folder', fullfile(root, 'second'));
u2_chk('ctrl_second_attach_new', @() strrep(run0(@() attach(d)), root, '<root>'));
u2_chk('ctrl_second_start', @() run0(@() start(d)));
u2_chk('ctrl_second_attach', @() run0(@() attach(d)));
u2_chk('ctrl_second_stop', @() run0(@() stop(d)));
u2_chk('ctrl_second_detach', @() run0(@() detach(d)));
e = mps.cache.control('k15third', 'MatlabTest', 'Folder', fullfile(root, 'second'));
u2_chk('ctrl_third_attach_gone', @() strrep(run0(@() attach(e)), root, '<root>'));
mkdir(fullfile(root, 'second'));
u2_chk('ctrl_third_attach', @() run0(@() attach(e)));
u2_chk('ctrl_third_start', @() run0(@() start(e)));
u2_chk('ctrl_third_stop', @() run0(@() stop(e)));
u2_chk('ctrl_third_detach', @() run0(@() detach(e)));
mps.cache.Connection.clear();
u2_chk('ctrl_list_all_cleared', @() size(mps.cache.Connection.list()));
rmdir(root, 's');

% --- helpers --------------------------------------------------------------------------------------
function chk(name, fn)
% A row recorded as a divergence of ADR 0271: a mutex that excludes, the archive name in a metric event,
% the echo of a logical, a struct as a dynamic field name, and enumeration() of a class JGraph has none of.
try
    v = fn();
    fprintf('CHK|%s|%s|div=ADR0271\n', name, u2_text(v));
catch err
    fprintf('CHK|%s|%s|div=ADR0271\n', name, err.identifier);
    fprintf('CHK|%s_msg|%s|div=ADR0271\n', name, u2_text(err.message));
end
end

function twoOut(f)
[a, b] = f(); %#ok<ASGLU>
end

function v = putGet(k, name, value)
put(k, name, value);
v = get(k, name);
end

function setBadKey(k)
k.('1bad') = 1;
end

function s = run0(f)
% A call with no outputs: 'ok', or the error it raised.
try
    f();
    s = 'ok';
catch err
    s = [err.identifier ' / ' strrep(err.message, newline, ' | ')];
end
end

function v = oneLine(f)
% A divergence row's value on one line in both engines: the answer, or the refusal's identifier.
try
    v = f();
catch err
    v = ['ERR ' err.identifier];
end
end

function s = shownPing(c)
s = strtrim(evalc('ping(c)'));
end

function s = warned(f)
lastwarn('', '');
f();
[msg, id] = lastwarn;
s = [id ' / ' msg];
end

function s = compositeKeys(k)
parts = {};
for i = 1:numel(k)
    if isstruct(k{i})
        parts{end + 1} = sprintf('struct(time=%d,id=%d)', k{i}.time, k{i}.id); %#ok<AGROW>
    else
        parts{end + 1} = k{i}; %#ok<AGROW>
    end
end
s = strjoin(sort(parts), ' ');
end

function tf = expired(m)
acquire(m);
pause(1.5);
tf = own(m);
end

function s = shownSorted(o)
% The display's lines sorted: R2025b orders a dynamicprops object's properties differently run to run.
txt = evalc('disp(o)');
txt = regexprep(txt, '<a [^>]*>|</a>', '');
lines = strtrim(splitlines(txt));
lines = sort(lines(~cellfun(@isempty, lines)));
s = strjoin(lines', ' / ');
end

function s = shown(o)
txt = evalc('disp(o)');
txt = regexprep(txt, '<a [^>]*>|</a>', '');
lines = strtrim(splitlines(txt));
lines = lines(~cellfun(@isempty, lines));
s = strjoin(lines', ' / ');
end
