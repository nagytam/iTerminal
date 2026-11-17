{ A small Pascal program: a greeting and the mean of some values. }
program HelloTotals;

const
  Count = 5;

type
  TValues = array[1..Count] of Real;

var
  Values: TValues;
  Total, Mean: Real;
  I: Integer;

begin
  WriteLn('Hello from iTerminal');
  Values[1] := 1.5;
  Values[2] := 2.0;
  Values[3] := 32.5;
  Values[4] := -0.4;
  Values[5] := 10;
  Total := 0;
  for I := 1 to Count do
    Total := Total + Values[I];
  Mean := Total / Count;
  if Mean > 5 then
    WriteLn('Mean is large: ', Mean:8:3)   // a line comment
  else
    WriteLn('It''s small');
end.
