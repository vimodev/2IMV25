import sys
import matplotlib.pyplot as plt
from mpl_toolkits import mplot3d
from skspatial.objects import Sphere

# Parse a vector from a printed form
# e.g '(1.0, 2.1, 3.2)' -> [1.0, 2.1, 3.2]
def vectorStringToArray(string):
    strings = string.replace(")", "").replace("(", "").split(", ")
    return [float(s) for s in strings]

# Plot the trajectory of the given experiment in 3D
def plotTrajectory3D(experiment):
    # Get the x, y and z sequences from the data
    trace = experiment['trace']
    x = [data['position'][0] for data in trace]
    y = [data['position'][1] for data in trace]
    z = [data['position'][2] for data in trace]
    # Create a figure
    fig = plt.figure()
    ax = plt.axes(projection='3d')
    # Set limits and labels
    ax.axes.set_xlim3d(left=0, right=1)
    ax.set_xlabel("X")
    ax.axes.set_ylim3d(bottom=0, top=1)
    # IMPORTANT: in matplotlib Z is up and Y is depth (cringe) so we swap them
    ax.set_ylabel("Z")
    ax.axes.set_zlim3d(bottom=0, top=1)
    ax.set_zlabel("Y")
    # Plot the trajectory
    ax.plot3D(x, z, y)
    s = experiment['source']
    t = experiment['target']
    # Plot spheres at source and target, again SWAP Y AND Z
    sourceSphere = Sphere([s[0], s[2], s[1]], experiment['sourceSize'] / 2)
    targetSphere = Sphere([t[0], t[2], t[1]], experiment['targetSize'] / 2)
    sourceSphere.plot_3d(ax, alpha=0.2, color='b')
    targetSphere.plot_3d(ax, alpha=0.2, color='g')
    # Show the visualization
    plt.show()

# Run the analysis on the given file
def main(filename):
    print("Opening: " + filename)
    print("")
    file = open(filename, "r")
    # Parse experiment number
    experiment_nr = int(file.readline().split(": ")[1])
    # Parse latency
    latency = int(file.readline().split(": ")[1])
    print("Experiment #" + str(experiment_nr) + " with latency of " + str(latency) + " ms")
    # Parse source and target information
    source = vectorStringToArray(file.readline().split(": ")[1])
    sourceSize = float(file.readline().split(": ")[1])
    target = vectorStringToArray(file.readline().split(": ")[1])
    targetSize = float(file.readline().split(": ")[1])
    print("Source: " + str(source) + " with diameter " + str(sourceSize) + " and Target: " + str(target) + " with diameter " + str(targetSize))
    print("")
    # Parse trace trajectory information
    print("Parsing trace")
    file.readline()
    file.readline()
    file.readline()
    trace = []
    # Read lines while we can
    while True:
        line = file.readline()
        if not line: break
        # Form a data point from each line
        datapoint = {}
        strings = [s.replace(";\n","") for s in line.split("; ")]
        # Parse timestamp, position and whether button was pressed at that point
        datapoint['time'] = float(strings[0])
        datapoint['position'] = vectorStringToArray(strings[1])
        trace.append(datapoint)
    # Convert time from time since Unity start up to time since experiment start
    startTime = trace[0]['time']
    for datapoint in trace:
        datapoint['time'] -= startTime
    # Calculate the duration
    duration = trace[len(trace)-1]['time']
    print("Trace duration: " + str(duration) + " seconds")
    # Form an experiment structure from all the parsed data
    experiment = {
        "trace": trace,
        "duration": duration,
        "source": source,
        "sourceSize": sourceSize,
        "target": target,
        "targetSize": targetSize
    }
    plotTrajectory3D(experiment)


if __name__ == "__main__":
    if len(sys.argv) == 1:
        print("Expected filename in argument")
        exit()
    main(sys.argv[1])