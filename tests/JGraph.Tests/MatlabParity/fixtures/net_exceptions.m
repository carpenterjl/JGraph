% net_exceptions.m -- NET.NetException (interop plan, stage 1): its class, the identifier each kind
% of member throws with, the message layout, ExceptionObject and its inner exception, and the
% reports. Messages are .NET 8's own text, which is why the runtime is pinned.

dotnetenv("core", Version="8");
p = interop_paths();
NET.addAssembly(p.assembly);
t = JGTest.Thrower();

cases = {
    'method',        @() t.Throw('boom')
    'static',        @() JGTest.Thrower.ThrowStatic()
    'ctor',          @() JGTest.Thrower(true)
    'getter',        @() t.Bad
    'custom',        @() JGTest.Thrower.ThrowCustom()
    'divzero',       @() JGTest.Thrower.DivideByZero(int32(1))
    'nullref',       @() JGTest.Thrower.NullRef()
    'bcl',           @() System.IO.File.ReadAllText('Z:\no\such\file.txt')
    'bcl_ctor',      @() System.Uri('not a uri')
    'bcl_static',    @() System.Int32.Parse('x')
    'createGeneric', @() NET.createGeneric('JGTest.Constrained', {'System.String'})
    };
for k = 1:size(cases, 1)
    key = cases{k, 1};
    try
        cases{k, 2}();
        ix_chk([key '_thrown'], false);
    catch e
        ix_chk([key '_class'], class(e));
        ix_chk([key '_identifier'], e.identifier);
        ix_chk([key '_message'], ix_flat(e.message));
        ix_chk([key '_ExceptionObject'], class(e.ExceptionObject));
        ix_chk([key '_Message'], char(e.ExceptionObject.Message));
        inner = e.ExceptionObject.InnerException;
        if isempty(inner)
            ix_chk([key '_inner'], 'none');
        else
            ix_chk([key '_inner'], [class(inner) ': ' char(inner.Message)]);
        end
        ix_chk([key '_cause'], numel(e.cause));
    end
end

try
    t.Settable = 1;
catch e
    ix_chk('setter_identifier', e.identifier);
    ix_chk('setter_message', ix_flat(e.message));
end

try
    JGTest.Thrower.ThrowCustom();
catch e
    ix_chk('custom_Code', e.ExceptionObject.Code);
    ix_chk('custom_isa_Exception', isa(e.ExceptionObject, 'System.Exception'));
    ix_chk('custom_isa_MException', isa(e, 'MException'));
    ix_chk('custom_properties', strjoin(properties(e)', ','));
    ix_chk('custom_rethrow', ix_id(@() rethrow(e)));
    ix_chk('custom_report_basic', ix_flat(getReport(e, 'basic', 'hyperlinks', 'off')), 'div=ADR0174');
end
