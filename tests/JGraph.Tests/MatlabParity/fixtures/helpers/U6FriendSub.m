classdef U6FriendSub < U6Friend
    % A subclass of a class named in an access list (u6_access.m).
    methods
        function r = subReadShared(obj, v)
            r = v.Shared;
        end
        function r = subCallForFriend(obj, v)
            r = forFriend(v);
        end
    end
end
