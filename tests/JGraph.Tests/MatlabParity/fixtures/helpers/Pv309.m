classdef Pv309
    % Property validation probe (open item 309).
    properties
        S (1,1) string
        N (1,1) double
        V (1,3) double
        R (1,:) double
        T (1,:) char
        L (1,1) logical
        I (1,1) int8
        U (1,1) uint16
        C (:,1) cell
        Sv (1,:) string
        Sd (1,1) string = "d"
        Nd (1,1) double = 7
        Nm (2,2) double
        Nc (:,:) double
        Pos (1,1) double {mustBePositive} = 1
        Cx (1,1) char = 'a'
        F (1,1) function_handle = @sin
        St (1,1) struct = struct('a', 1)
        Dt (1,1) datetime = datetime(2020, 1, 1)
        Free
    end
end
