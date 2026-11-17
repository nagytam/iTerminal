-- A small PostgreSQL script: readings, a summary and an upsert.
CREATE TABLE readings (
    id     serial PRIMARY KEY,
    sensor text NOT NULL UNIQUE,
    value  numeric(9, 2) NOT NULL DEFAULT 0
);

INSERT INTO readings (sensor, value)
VALUES ('alpha', 1.50), ('beta', 32.50), ('gamma', -0.40)
ON CONFLICT (sensor) DO UPDATE SET value = EXCLUDED.value
RETURNING id, sensor;

/* A block comment
   over two lines. */
WITH summary AS (
    SELECT avg(value) AS mean_value, count(*) AS n
    FROM readings
    WHERE value > 0
)
SELECT s.mean_value::numeric(9, 2), s.n
FROM summary AS s
WHERE s.n > 1;

SELECT 'Hello from iTerminal' AS greeting, sensor
FROM readings
WHERE sensor ILIKE 'b%'
ORDER BY sensor
LIMIT 10;
