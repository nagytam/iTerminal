// A small Swift program: a greeting and the mean of some values.
import Foundation

struct Summary {
    let values: [Double]

    func mean() -> Double {
        guard !values.isEmpty else { return 0 }
        return values.reduce(0, +) / Double(values.count)
    }
}

let summary = Summary(values: [1.5, 2.0, 3.25e1, -4.0e-1, 10])
print("Hello from iTerminal")
if summary.mean() > 5 {
    print("Mean is large: \(summary.mean())")
} else {
    print("Mean is small")
}
/* Multi-line comments
   use slash-star. */
let mask = 0xFF_FF
