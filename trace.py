import sys
import os
from tkinter import Y
import matplotlib.pyplot as plt
from mpl_toolkits import mplot3d
from skspatial.objects import Sphere
import math
import numpy as np
from sklearn.linear_model import LinearRegression

# Parse a vector from a printed form
# e.g '(1.0, 2.1, 3.2)' -> [1.0, 2.1, 3.2]
def vectorStringToArray(string):
    strings = string.replace(")", "").replace("(", "").split(", ")
    return [float(s) for s in strings]

def distance(v1, v2):
    return math.sqrt((v1[0] - v2[0])**2 + (v1[1] - v2[1])**2 + (v1[2] - v2[2])**2)

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
    originSphere = Sphere([0, 0, 0], 0.025)
    sourceSphere.plot_3d(ax, alpha=0.2, color='b')
    targetSphere.plot_3d(ax, alpha=0.2, color='g')
    originSphere.plot_3d(ax, alpha=1, color='r')
    # Show the visualization
    plt.show()

def loadFile(filename):
    file = open(filename, "r")
    # Parse experiment number
    experiment_nr = int(file.readline().split(": ")[1])
    # Parse latency
    latency = int(file.readline().split(": ")[1])
    # print("Experiment #" + str(experiment_nr) + " with latency of " + str(latency) + " ms")
    # Parse source and target information
    source = vectorStringToArray(file.readline().split(": ")[1])
    sourceSize = float(file.readline().split(": ")[1])
    target = vectorStringToArray(file.readline().split(": ")[1])
    targetSize = float(file.readline().split(": ")[1])
    # print("Source: " + str(source) + " with diameter " + str(sourceSize) + " and Target: " + str(target) + " with diameter " + str(targetSize))
    # print("")
    # Parse trace trajectory information
    # print("Parsing trace")
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
    # print("Trace duration: " + str(duration) + " seconds")
    # Form an experiment structure from all the parsed data
    experiment = {
        "trace": trace,
        "latency": latency,
        "experiment_nr": experiment_nr,
        "duration": duration,
        "success": distance(trace[len(trace)-1]['position'], target) <= targetSize / 2,
        "source": source,
        "sourceSize": sourceSize,
        "target": target,
        "targetSize": targetSize
    }
    return experiment

def ingest(root):
    # Get all subdirs of root
    dirs = [f for f in os.listdir(root) if os.path.isdir(os.path.join(root, f))]
    experiments = []
    # Load all trace files of each subdir
    for dir in dirs:
        files = [f for f in os.listdir(os.path.join(root, dir)) if f.endswith('.txt')]
        experiments += [loadFile(os.path.join(root, dir, f)) for f in files]
    print()
    print("Loaded " + str(len(experiments)) + " experiments")
    # Organize them on latency and experiment number
    results = {}
    for experiment in experiments:
        latency = experiment['latency']
        if latency not in results.keys():
            results[latency] = [[] for i in range(9)]
        results[latency][experiment['experiment_nr']].append(experiment)
    return results

# Given a list of results from a particular experiment, calculate its ID
def calculateID(experiment):
    e = experiment[0]
    return math.log2(distance(e['source'], e['target']) / e['targetSize'] + 1)

# Plot the mean response times against ID for each experiment and latency
def plotMeanTimes(meanTimes, ID):
    plt.figure()
    for latency in meanTimes:
        plt.plot(ID, meanTimes[latency], label=str(latency) + " ms")
    plt.legend(loc='upper left')
    plt.title("Mean times per ID and latency including misses")
    plt.ylabel("Mean time (seconds)")
    plt.xlabel("ID=log2(D/W + 1.0)")
    plt.show()

def fittsRegression(meanTimes, ID):
    X = []
    y = []
    for latency in meanTimes:
        for i in range(len(meanTimes[latency])):
            if meanTimes[latency][i] is not None:
                X.append([ID[i], latency*ID[i] / 1000])
                y.append(meanTimes[latency][i])
    X = np.array(X)
    y = np.array(y)
    reg = LinearRegression().fit(X, y)
    print("Linear regression fit with R2: " + str(reg.score(X,y)))
    c1 = reg.intercept_
    c2 = reg.coef_[1]
    c3 = reg.coef_[0] / c2
    print("c1: " + str(c1), "c2: " + str(c2), "c3: " + str(c3))


# perform some analysis in terms of Fitts law
def fitts(results):
    # Sort the experiments
    for latency in results:
        results[latency].sort(key=calculateID)
    # List of IDs
    ID = [calculateID(e) for e in results[0]]
    # Compute mean response time for each experiment and latency combination
    meanTimes = {}
    for latency in results:
        meanTimes[latency] = []
        for experiment in results[latency]:
            mean = 0
            for take in experiment:
                mean += take['duration']
            mean /= len(experiment)
            meanTimes[latency].append(mean)
    # Plot the mean times
    plotMeanTimes(meanTimes, ID)
    fittsRegression(meanTimes, ID)
    

def plotMeanTimes_noerror(meanTimes, ID):
    plt.figure()
    for latency in meanTimes:
        IDl = []
        latency_noerror = []
        for i in range(len(meanTimes[latency])):
            if meanTimes[latency][i] is not None:
                latency_noerror.append(meanTimes[latency][i])
                IDl.append(ID[i])
        plt.plot(IDl, latency_noerror, label=str(latency) + " ms")
    plt.legend(loc='upper left')
    plt.title("Mean times per ID and latency without misses")
    plt.ylabel("Mean time (seconds)")
    plt.xlabel("ID=log2(D/W + 1.0)")
    plt.show()

# perform some analysis in terms of Fitts law
def fitts_noerror(results):
    # Sort the experiments
    for latency in results:
        results[latency].sort(key=calculateID)
    # List of IDs
    ID = [calculateID(e) for e in results[0]]
    # Compute mean response time for each experiment and latency combination
    meanTimes = {}
    for latency in results:
        meanTimes[latency] = []
        for experiment in results[latency]:
            mean = 0
            successes = 0
            for take in experiment:
                if take['success']:
                    mean += take['duration']
                    successes += 1
            if successes == 0:
                mean = None
            else:
                mean /= successes
            meanTimes[latency].append(mean)
    # Plot the mean times

    plotMeanTimes_noerror(meanTimes, ID)
    fittsRegression(meanTimes, ID)

# Run the analysis on the given file
def main(root):
    results = ingest(root)
    fitts(results)
    fitts_noerror(results)


if __name__ == "__main__":
    if len(sys.argv) == 1:
        print("Expected filename in argument")
        exit()
    main(sys.argv[1])