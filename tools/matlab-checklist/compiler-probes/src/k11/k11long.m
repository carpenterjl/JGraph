function [first_output, second_output, third_output] = k11long(alpha_input, beta_input, gamma_input, delta_input, epsilon_input)
%K11LONG Long argument names, so the generated declarations are long: sums of the inputs.
first_output = alpha_input + beta_input;
second_output = gamma_input + delta_input;
third_output = epsilon_input;
end
