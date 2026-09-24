classdef CallBox
    % A value class whose methods report the call contract (V9, ADR 0170): the output count a
    % method call asked for, the caller's argument names, and an output left unassigned.
    methods
        function y = m(obj) %#ok<MANU>
            vlog(sprintf('%d', nargout));
            y = 0;
        end
        function [a, b] = m2(obj) %#ok<MANU>
            vlog(sprintf('%d', nargout));
            a = 1;
            b = 2;
        end
        function noout(obj) %#ok<MANU>
            vlog('noout');
        end
        function v = inp(obj, x) %#ok<INUSD>
            vlog(sprintf('[%s][%s]', inputname(1), inputname(2)));
            v = 0;
        end
        function y = m_unassigned(obj) %#ok<MANU,STOUT>
        end
    end
end
