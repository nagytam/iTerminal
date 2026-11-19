% A small MATLAB script: a greeting and the mean of some values.
function result = summarize(values)
    result = mean(values);
end

disp("Hello from iTerminal")
values = [1.5, 2.0, 3.25e1, -4.0e-1, 10];
total = 0;
for k = 1:numel(values)
    total = total + values(k);
end
if total > 5
    fprintf('Total is %g\n', total);
else
    disp('Total is small');
end
%{
Block comments use percent-brace
on lines of their own.
%}
result = summarize(values)
