// Every test here runs the whole application: its render threads at the highest thread priority
// (spinning with a 1 ms sleep), its input thread above normal, and every background job. Two at once
// starve each other's renderers and turn the waits into timing tests, so they run one after another.
[assembly: DoNotParallelize]
