% Open item 28 (ADR 0220): an anonymous function called for nothing calls its body for nothing, so a
% function that declares an output it never sets runs quietly; audiodevinfo's six-argument form is
% refused however it is called, because audiodevinfo.m asks its own helper for devInfo; an evalc
% inside another captures its own text; and a method named before its class was ever used is found
% once the arguments have loaded the class. Probes probe_28 to probe_28f (open-items scratch).

% The first line to name OiArea: areaOf is looked for after its argument has loaded the class.
u9b_chk('first_use_method', @() areaOf(OiArea()));
u9b_chk('anon_unassigned', @() as_statement(@() declares_unset()));
u9b_chk('anon_varargout_none', @() as_statement(@() varargout_unset()));
u9b_chk('anon_varargout_one', @() squash(evalc('call_none(@() varargout_one())')));
u9b_chk('anon_nested', @() squash(evalc('call_none(@() feval(@() max([3 4])))')));
u9b_chk('audiodevinfo_six_statement', @() as_statement(@() audiodevinfo(1, 2, 3, 4, 5, 6)));
six = @() audiodevinfo(1, 2, 3, 4, 5, 6);
u9b_chk('audiodevinfo_six_anon', @() as_statement(six));
u9b_chk('evalc_nested', @() squash(evalc('disp(evalc(''disp(1)''))')));
outer = evalc('inner = evalc(''disp(2)''); disp(3)');
u9b_chk('evalc_nested_split', @() {squash(outer), squash(inner)});

function r = as_statement(fn)
% 'ran', or the refusal's identifier and sentence.
try
    fn();
    r = 'ran';
catch err
    r = [err.identifier ' | ' err.message];
end
end

function s = squash(s)
% The text with its layout's spacing folded, so a line says what was captured.
s = strtrim(regexprep(s, '\s+', ' '));
end

function call_none(f)
f()
end

function y = declares_unset() %#ok<STOUT>
end

function varargout = varargout_unset()
end

function varargout = varargout_one()
varargout{1} = 7;
end
