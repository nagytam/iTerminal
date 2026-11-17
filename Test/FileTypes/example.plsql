-- A small PL/SQL package body: a greeting and the mean of some readings.
CREATE OR REPLACE PACKAGE BODY readings_pkg AS

  FUNCTION mean (p_values IN SYS.ODCINUMBERLIST) RETURN NUMBER IS
    v_total NUMBER := 0;
  BEGIN
    IF p_values.COUNT = 0 THEN
      RETURN 0;
    END IF;
    FOR i IN 1 .. p_values.COUNT LOOP
      v_total := v_total + p_values(i);
    END LOOP;
    RETURN v_total / p_values.COUNT;
  END mean;

  PROCEDURE report IS
    v_mean NUMBER;
    v_values SYS.ODCINUMBERLIST := SYS.ODCINUMBERLIST(1.5, 2.0, 32.5, -0.4, 10);
  BEGIN
    v_mean := mean(v_values);
    DBMS_OUTPUT.PUT_LINE('Hello from iTerminal');
    IF v_mean > 5 THEN
      DBMS_OUTPUT.PUT_LINE('Mean is large: ' || TO_CHAR(v_mean, '999.999'));
    ELSE
      DBMS_OUTPUT.PUT_LINE('It''s small');
    END IF;
    /* A block comment
       over two lines. */
  EXCEPTION
    WHEN ZERO_DIVIDE THEN
      RAISE_APPLICATION_ERROR(-20001, 'No readings');
  END report;

END readings_pkg;
/
