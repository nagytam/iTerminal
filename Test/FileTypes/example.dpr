// A small Delphi console program: a greeting and the mean of some values.
program HelloTotals;

{$APPTYPE CONSOLE}

uses
  System.SysUtils;

type
  TSummary = class
  private
    FValues: array of Double;
  public
    constructor Create(const AValues: array of Double);
    function Mean: Double;
  end;

constructor TSummary.Create(const AValues: array of Double);
var
  I: Integer;
begin
  inherited Create;
  SetLength(FValues, Length(AValues));
  for I := 0 to High(AValues) do
    FValues[I] := AValues[I];
end;

function TSummary.Mean: Double;
var
  I: Integer;
  Total: Double;
begin
  Total := 0;
  for I := 0 to High(FValues) do
    Total := Total + FValues[I];
  if Length(FValues) > 0 then
    Result := Total / Length(FValues)
  else
    Result := 0;
end;

var
  Summary: TSummary;
begin
  try
    Summary := TSummary.Create([1.5, 2.0, 32.5, -0.4, 10]);
    try
      WriteLn('Hello from iTerminal');
      if Summary.Mean > 5 then
        WriteLn('Mean is large: ', Summary.Mean:8:3)
      else
        WriteLn('Mean is small');
    finally
      Summary.Free;
    end;
  except
    on E: Exception do
      WriteLn('Error: ', E.Message);
  end;
end.
