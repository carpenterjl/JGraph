% probe_net_exceptions: NET.NetException from every kind of member, its fields, and its report.
dotnetenv("core", Version="8");
a = ip_assets();
NET.addAssembly(a.assembly);
t = JGTest.Thrower();

cases = {
    'method',       't.Throw(''boom'')'
    'static',       'JGTest.Thrower.ThrowStatic()'
    'ctor',         'JGTest.Thrower(true)'
    'getter',       't.Bad'
    'setter',       't.Settable = 1'
    'custom',       'JGTest.Thrower.ThrowCustom()'
    'divzero',      'JGTest.Thrower.DivideByZero(int32(1))'
    'nullref',      'JGTest.Thrower.NullRef()'
    'bcl',          'System.IO.File.ReadAllText(''Z:\no\such\file.txt'')'
    'bcl.ctor',     'System.Uri(''not a uri'')'
    'bcl.static',   'System.Int32.Parse(''x'')'
    'createGeneric','NET.createGeneric(''JGTest.Constrained'', {''System.String''})'
    };
for k = 1:size(cases, 1)
    key = cases{k, 1};
    try
        evalin('base', [cases{k, 2} ';']);
        fprintf('%s\tno error\n', key);
    catch e
        fprintf('%s.class\t%s\n', key, class(e));
        fprintf('%s.identifier\t%s\n', key, e.identifier);
        fprintf('%s.message\t%s\n', key, strrep(e.message, newline, ' | '));
        if isa(e, 'NET.NetException')
            fprintf('%s.ExceptionObject\t%s\n', key, class(e.ExceptionObject));
            fprintf('%s.Message\t%s\n', key, char(e.ExceptionObject.Message));
            inner = e.ExceptionObject.InnerException;
            if isempty(inner)
                fprintf('%s.inner\t[]\n', key);
            else
                fprintf('%s.inner\t%s %s\n', key, class(inner), char(inner.Message));
            end
            fprintf('%s.cause\t%d\n', key, numel(e.cause));
            fprintf('%s.stack.n\t%d\n', key, numel(e.stack));
        end
        r = getReport(e, 'basic');
        fprintf('%s.report.basic\t%s\n', key, strrep(r, newline, ' | '));
        r = getReport(e, 'extended', 'hyperlinks', 'off');
        fprintf('%s.report.extended\t%s\n', key, strrep(r, newline, ' | '));
    end
end

% ---- the custom exception's own members
try
    JGTest.Thrower.ThrowCustom();
catch e
    ip_pr('custom.Code', 'e.ExceptionObject.Code');
    ip_pr('custom.isa', 'isa(e.ExceptionObject, ''System.Exception'')');
    ip_pr('custom.fields', 'properties(e)');
    ip_pr('custom.methods', 'methods(e)');
    ip_px('custom.disp', 'e');
    ip_px('custom.rethrow', 'rethrow(e)');
    ip_px('custom.throw', 'throw(e)');
end

% ---- what an uncaught one prints (the command-window form)
ip_px('uncaught.evalc', 'JGTest.Thrower.ThrowStatic()');
ip_pr('lasterr', 'lasterr');
ip_pr('MException.last', 'MException.last(''reset'')');
