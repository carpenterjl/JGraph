% Open items 14, 27, 29 and 34 (ADR 0216): what a class says about itself, and a file read as text.
% methods asked for nothing prints R2025b's listing for a user class, a struct and a function handle,
% and "No class" for a name that is none; -full gives each method's signature. superclasses asked for
% nothing prints its own listing. feature('getpid') is the process id. struct of an object warns and
% answers every property, a refusing getter's left out. fread keeps char when the precision says char,
% and fileread reads a file whole. Listings are compared line by line. Probe probe_b6 (open-items
% scratch).

% --- methods (item 14) ---
u9b_chk('m_plain', @() listing('methods(''OiBase'')'));
u9b_chk('m_value_only_ctor', @() listing('methods(''OiValue'')'));
u9b_chk('m_kid', @() listing('methods(''OiKid'')'));
u9b_chk('m_kid_obj', @() listing('methods(OiKid())'));
u9b_chk('m_base_full', @() listing('methods(''OiBase'', ''-full'')'));
u9b_chk('m_kid_full', @() listing('methods(''OiKid'', ''-full'')'));
u9b_chk('m_value_full', @() listing('methods(''OiValue'', ''-full'')'));
u9b_chk('m_shape_full', @() listing('methods(''OiShape'', ''-full'')'));
u9b_chk('m_copykid_full', @() listing('methods(''OiCopyKid'', ''-full'')'));
% a handle class's last line is "Methods of X inherited from handle." with two hyperlinks in R2025b
u9b_chkdiv('m_shape', @() listing('methods(''OiShape'')'), '0216');
u9b_chkdiv('m_copykid', @() listing('methods(''OiCopyKid'')'), '0216');
u9b_chk('m_shape_full_cell', @() methods('OiShape', '-full'));
u9b_chk('m_kid_full_cell', @() methods('OiKid', '-full'));
u9b_chk('m_shape_names', @() methods('OiShape'));
u9b_chk('m_kid_names', @() methods(OiKid()));
u9b_chk('m_struct', @() listing('methods(struct(''a'', 1))'));
u9b_chk('m_struct_names', @() methods(struct('a', 1)));
u9b_chk('m_handle_fn', @() listing('methods(@sin)'));
u9b_chk('m_handle_fn_names', @() methods(@sin));
u9b_chk('m_unknown', @() listing('methods(''NoSuchClassOi'')'));
u9b_chk('m_unknown_full', @() listing('methods(''NoSuchClassOi'', ''-full'')'));
u9b_chk('m_unknown_answer', @() methods('NoSuchClassOi'));
u9b_chk('m_command', @() listing('methods OiBase'));

% --- superclasses and feature (item 27) ---
u9b_chk('sc_handle', @() superclasses('OiPlainHandle'));
u9b_chk('sc_value', @() superclasses('OiValue'));
u9b_chk('sc_two', @() superclasses('OiCopyKid'));
u9b_chk('sc_obj', @() superclasses(OiCopyKid()));
u9b_chk('sc_string', @() superclasses("OiKid"));
u9b_chk('sc_double', @() superclasses('double'));
u9b_chk('sc_number', @() superclasses(5));
u9b_chk('sc_unknown', @() superclasses('NoSuchClassOi'));
u9b_chk('sc_extra', @() superclasses('OiKid', 1));
u9b_chk('sc_listing', @() listing('superclasses(''OiCopyKid'')'));
u9b_chk('sc_listing_none', @() listing('superclasses(''double'')'));
u9b_chk('sc_listing_value', @() listing('superclasses(OiValue())'));
u9b_chk('pid_class', @() class(feature('getpid')));
u9b_chk('pid_size', @() size(feature('getpid')));
u9b_chk('pid_whole', @() feature('getpid') == round(feature('GETPID')) && feature('getpid') > 0);
u9b_chk('feature_none', @() feature());

% --- struct of an object (item 29) ---
u9b_chk('st_value', @() struct_quiet(OiValue()));
u9b_chk('st_warning', @() struct_warning(OiValue()));
u9b_chk('st_handle', @() struct_quiet(OiPlainHandle()));
u9b_chk('st_order', @() fieldnames(struct_quiet(OiCopyKid())));
u9b_chk('st_getters', @() struct_quiet(OiGetters()));
a = OiValue(); a.X = 10;
b = OiValue(); b.X = 20;
u9b_chk('st_array_size', @() size(struct_quiet([a, b])));
u9b_chk('st_array_first', @() getfield(struct_quiet([a, b]), 'X'));

% --- fread's char precisions and fileread (item 34) ---
fn = [tempname '.txt'];
fid = fopen(fn, 'w'); fwrite(fid, 'abcdefgh'); fclose(fid);
u9b_chk('fr_star_char', @() read_as(fn, {'*char'}));
u9b_chk('fr_row_char', @() read_as(fn, {[1 inf], '*char'}));
u9b_chk('fr_u8_char', @() read_as(fn, {inf, 'uint8=>char'}));
u9b_chk('fr_char_char', @() read_as(fn, {3, 'char=>char'}));
u9b_chk('fr_char_double', @() read_as(fn, {3, 'char'}));
u9b_chk('fr_square_char', @() read_as(fn, {[2 2], '*char'}));
u9b_chk('fr_one_char', @() read_as(fn, {1, '*char'}));
u9b_chk('fr_star_u16', @() read_as(fn, {2, '*uint16'}));
u9b_chk('fileread_text', @() fileread(fn));
u9b_chk('fileread_string_name', @() fileread(string(fn)));
u9b_chk('fileread_case', @() fileread(fn, 'encoding', 'UTF-8'));
fid = fopen(fn, 'w'); fwrite(fid, [104 105 13 10 195 169 10]); fclose(fid);
u9b_chk('fileread_utf8', @() double(fileread(fn)));
u9b_chk('fileread_latin1', @() double(fileread(fn, 'Encoding', 'ISO-8859-1')));
fid = fopen(fn, 'w'); fwrite(fid, [239 187 191 97 98]); fclose(fid);
u9b_chk('fileread_bom', @() double(fileread(fn)));
fid = fopen(fn, 'w'); fclose(fid);
u9b_chk('fileread_empty', @() size(fileread(fn)));
u9b_chk('fileread_bad_name', @() fileread(fn, 'Bogus', 'x'));
u9b_chk('fileread_odd', @() fileread(fn, 'Encoding'));
u9b_chk('fileread_bad_encoding', @() fileread(fn, 'Encoding', 'nonsense-enc'));
delete(fn);
u9b_chk('fileread_missing', @() fileread('no_such_file_oi.txt'));
u9b_chk('fileread_number', @() fileread(5));
u9b_chk('fileread_cell', @() fileread({'a.txt'}));
u9b_chk('fileread_empty_name', @() fileread(''));

function lines = listing(code)
% What a statement prints, a line to a cell.
lines = strsplit(evalc(code), newline);
end

function s = struct_quiet(obj)
state = warning('off', 'MATLAB:structOnObject');
s = struct(obj);
warning(state);
end

function id = struct_warning(obj)
state = warning('off', 'MATLAB:structOnObject');
lastwarn('');
struct(obj);
[~, id] = lastwarn();
warning(state);
end

function v = read_as(fn, rest)
fid = fopen(fn);
v = fread(fid, rest{:});
fclose(fid);
end
