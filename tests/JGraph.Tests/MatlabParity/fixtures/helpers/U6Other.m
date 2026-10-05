classdef U6Other
    % Named in one of U6Vault's two-class lists and not in its one-class ones (u6_access.m).
    methods
        function r = readListed(obj, v)
            r = v.Listed;
        end
        function r = readShared(obj, v)
            r = v.Shared;
        end
        function r = callForListed(obj, v)
            r = forListed(v);
        end
        function r = callForFriend(obj, v)
            r = forFriend(v);
        end
    end
end
