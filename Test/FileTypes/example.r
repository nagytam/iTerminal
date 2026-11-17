# A small R script: a greeting and some summary statistics.
greet <- function(name) {
  paste("Hello from", name)
}

values <- c(1.5, 2.0, 3.25, -4.0, 10L)
total <- 0
for (v in values) {
  if (is.na(v)) next
  total <- total + v
}
mean_value <- total / length(values)
if (mean_value > 5) {
  print(greet("iTerminal"))
} else {
  cat('Mean is', mean_value, '\n')
}
repeat {
  break
}
