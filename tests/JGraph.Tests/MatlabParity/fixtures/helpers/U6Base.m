classdef U6Base < handle
    % The root of the U6 handle hierarchy (u6_inherit.m).
    properties
        Id = 0
    end
    events
        Ping
    end
    methods
        function obj = U6Base(id)
            vlog(['Base:' num2str(nargin)]);
            if nargin > 0
                obj.Id = id;
            end
        end
        function delete(obj)
            vlog(['delBase:' class(obj)]);
        end
        function fire(obj)
            notify(obj, 'Ping');
        end
        function r = tag(obj)
            r = ['base' num2str(obj.Id)];
        end
        function bump(obj)
            obj.Id = obj.Id + 1;
        end
    end
end
