function r = ix_imp_forms(which)
% IX_IMP_FORMS  One import form per call; each branch is its own local function, so each has only
%   its own imports (an import belongs to the function it is written in).
switch which
    case 'type', r = type_form();
    case 'typewild', r = typewild_form();
    case 'member', r = member_form();
    case 'user', r = user_form();
    case 'usertype', r = usertype_form();
    case 'conflict', r = conflict_form();
    case 'fncall', r = fncall_form();
    case 'late', r = late_form();
    case 'branch', r = branch_form(false);
    case 'nested', r = nested_form();
    case 'anon', r = anon_form();
    case 'eval', r = eval_form();
    case 'callee', r = callee_form();
end
end

function r = type_form()
import System.Math
r = {'Math', ix_try(@() Math.Max(1, 2)); 'bare', ix_try(@() Max(1, 2))};
end

function r = typewild_form()
import System.Math.*
r = {'Max', ix_try(@() Max(1, 2)); 'PI', ix_try(@() PI)};
end

function r = member_form()
import System.Math.Max
r = {'Max', ix_try(@() Max(1, 2))};
end

function r = user_form()
import JGTest.*
r = {
    'Members',   ix_try(@() Members(3).Value)
    'Statics',   ix_try(@() Statics.Hello())
    'plot',      ix_try(@() class(plot))
    'Sum_Of',    ix_try(@() Sum.Of([1 2]))
    'sum',       ix_try(@() sum([1 2]))
    'max_Thing', ix_try(@() max.Thing().Where())
    'max_fn',    ix_try(@() max([1 3]))
    };
end

function r = usertype_form()
import JGTest.plot
r = {'plot', ix_try(@() class(plot))};
end

function r = conflict_form()
import System.Timers.*
import System.Threading.*
r = {'Timer', ix_try(@() class(Timer(100))); 'list', ix_show(import)};
end

function r = fncall_form()
L = import('System.Math');
r = {'L', ix_show(L); 'Math', ix_try(@() Math.Max(1, 2))};
end

function r = late_form()
x = Max(4, 5);
import System.Math.*
r = {'late', ix_show(x)};
end

function r = branch_form(flag)
if flag
    import System.Math.*
end
r = {'branch_not_taken', ix_try(@() Max(4, 5))};
end

function r = nested_form()
import System.Math.*
r = {'nested', ix_show(inner())};
    function v = inner()
        v = Max(4, 5);
    end
end

function r = anon_form()
f = anon_maker();
r = {'anon', ix_show(f())};
end

function f = anon_maker()
import System.Math.*
f = @() Max(4, 5);
end

function r = eval_form()
import System.Math
r = {'eval', ix_show(eval('Math.Max(1, 2)'))};
end

function r = callee_form()
import System.Math.*
r = {'local_fn', ix_try(@() local_max()); 'callee_list', ix_show(ix_imp_callee())};
end

function v = local_max()
v = Max(4, 5);
end
