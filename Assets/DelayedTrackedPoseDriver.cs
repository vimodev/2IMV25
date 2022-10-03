using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.SpatialTracking;
using System.IO;
using System;
using TMPro;

// This class contains all the information to run a single experiment
class Experiment {

    // Transform the coordinate from [-1,1]^3 to [0,1]^3
    public static Vector3 toExperimentOrigin(Vector3 p) {
        Vector3 v = new Vector3(p.x, p.y, p.z);
        v = v + new Vector3(1, 1, 1);
        v = v / 2;
        return v;
    }

    // Transform the coordinate from [0,1]^3 to [-1,1]^3
    public static Vector3 toGameOrigin(Vector3 p) {
        Vector3 v = new Vector3(p.x, p.y, p.z);
        v = v * 2;
        v = v - new Vector3(1, 1, 1);
        return v;
    }

    // Transform the scale from [0, 2] to [0,1]
    public static float toExperimentScale(float scale) {
        return scale / 2;
    }

    // Transform the scale from [0, 1] to [0,2]
    public static float toGameScale(float scale) {
        return scale * 2;
    }

    // Source location and diameter
    public Vector3 sourceLocation;
    public float sourceDiameter;
    // Target location and diameter
    public Vector3 targetLocation;
    public float targetDiameter;
    // Set all the information
    public Experiment(Vector3 s, float sd, Vector3 t, float td) {
        this.sourceLocation = toGameOrigin(s);
        this.sourceDiameter = toGameScale(sd);
        this.targetLocation = toGameOrigin(t);
        this.targetDiameter = toGameScale(td);
    }
}

public class DelayedTrackedPoseDriver : TrackedPoseDriver {
    
    // The TextMeshPro component for setting text
    TextMeshPro textMesh;

    // Templates for source and target
    GameObject sourceTemplate;
    GameObject targetTemplate;
    // Currently active source and target
    GameObject activeSource;
    GameObject activeTarget;
    // The wireframe bounding box
    GameObject bounds;
    // The pointer attached to the controller
    GameObject pointer;

    // List of experiments to run
    List<Experiment> experiments = new List<Experiment>(new Experiment[] {
        new Experiment(new Vector3(0.75f, 0.1f, 0.2f), 0.2f,
                        new Vector3(0.1f, 0.5f, 0.2f), 0.1f),
        new Experiment(new Vector3(0.9f, 0.3f, 0.25f), 0.05f,
                        new Vector3(0.1f, 0.5f, 0.75f), 0.25f)
    });
    int level;
    List<int> latencies = new List<int>(new int[] {
        0, 150, 300, 450, 600, 750
    });
    int latencyIndex;

    // The controller input device to listen for buttons
    InputDevice controller;
    // Whether the above variable has been set yet (InputDevice can not be null)
    bool controllerSet;
    // Was the button pressed previous tick?
    bool buttonPrevious;

    // Trace file
    public string traceFileName;
    // Is a trace running?
    private bool tracing;
    // If trace is running, this is the write stream
    private StreamWriter traceWriter;

    // Queues for delaying input
    public Queue<Vector3> positionQueue = new Queue<Vector3>();
    public Queue<Quaternion> rotationQueue = new Queue<Quaternion>();
    public Queue<PoseDataFlags> flagQueue = new Queue<PoseDataFlags>();
    public Queue<float> timeQueue = new Queue<float>();

    public void setText(String text) {
        if (textMesh == null) {
            textMesh = GameObject.Find("Text").GetComponent<TextMeshPro>();
        }
        textMesh.text = text;
    }

    // Start running a trace, and open a file write stream
    public void startTracing() {
        if (tracing) { stopTracing(); }
        // Form file name
        traceFileName = DateTime.Now.ToString("yyyyMMddTHHmmssff") + "_trace_" + level.ToString() + ".txt";
        traceWriter = File.AppendText(traceFileName);
        // Write experiment information
        traceWriter.WriteLine("Experiment: " + level.ToString());
        traceWriter.WriteLine("Latency: " + latencies[latencyIndex].ToString());
        traceWriter.WriteLine("Source: " + Experiment.toExperimentOrigin(activeSource.transform.localPosition).ToString());
        traceWriter.WriteLine("SourceSize: " + Experiment.toExperimentScale(activeSource.transform.localScale.x).ToString("0.0000"));
        traceWriter.WriteLine("Target: " + Experiment.toExperimentOrigin(activeTarget.transform.localPosition).ToString());
        traceWriter.WriteLine("TargetSize: " + Experiment.toExperimentScale(activeTarget.transform.localScale.x).ToString("0.0000"));
        traceWriter.WriteLine("");
        // Write header for trace section
        traceWriter.WriteLine("Trace:");
        traceWriter.WriteLine("time; position; button;");
        tracing = true;
    }

    // Stop a running trace, and close the write stream
    public void stopTracing() {
        if (!tracing) { return; }
        traceWriter.Close();
        traceWriter = null;
        tracing = false;
    }

    // Get the XR controller
    public InputDevice getController() {
        var rightHandedControllers = new List<InputDevice>();
        var desiredCharacteristics = InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Right;
        InputDevices.GetDevicesWithCharacteristics(desiredCharacteristics, rightHandedControllers);
        return rightHandedControllers[0];
    }

    // This is run as soon as the controller is detected
    public void Start() {
        tracing = false;
        controllerSet = false;
        level = 0;
        latencyIndex = 0;
    }

    // On quit, if we are still tracing, stop
    void OnApplicationQuit() {
        stopTracing();
    }

    // Is the interaction button being pressed?
    public bool buttonPressed() {
        if (controllerSet == false) { controller = getController(); controllerSet = true; buttonPrevious = false;}
        bool buttonValue;
        controller.TryGetFeatureValue(CommonUsages.menuButton, out buttonValue);
        bool returnValue = !buttonPrevious && buttonValue;
        buttonPrevious = buttonValue;
        return returnValue;
    }

    // Is the center of the pointer sphere currently colliding with the given game object
    public bool isColliding(GameObject obj) {
        if (obj == null) return false;
        if (obj.GetComponent<Collider>() == null) return false;
        return obj.GetComponent<Collider>().bounds.Contains(pointer.transform.position);
    }

    /**
        This sets the transformation of the in game controller based on transformations recieved by the motion controller
        We purposefully delay these transformations by queueing them up
        We sloppily use this function to do all other continuous stuff :)
    */
    protected override void SetLocalTransform(Vector3 newPosition, Quaternion newRotation, PoseDataFlags poseFlags) {
        // If game objects are not set yet, set them (can not be done at start due to random load order)
        if (sourceTemplate == null) {
            sourceTemplate = GameObject.Find("SourceTemplate");
        }
        if (targetTemplate == null) {
            targetTemplate = GameObject.Find("TargetTemplate");
        }
        if (bounds == null) {
            bounds = GameObject.Find("bounding_box");
        }
        if (pointer == null) {
            pointer = GameObject.Find("Pointer");
        }
        // If active source and target are null, we start a new experiment
        if (level >= experiments.Count && latencyIndex < latencies.Count) { 
            level = 0; latencyIndex++; 
            timeQueue.Clear(); flagQueue.Clear(); positionQueue.Clear(); rotationQueue.Clear();
        }
        if (activeSource == null && activeTarget == null && level < experiments.Count) {
            Debug.Log("Spawning source and target: level " + level.ToString());
            setText("Experiment " + level.ToString() + " (" + latencies[latencyIndex].ToString() + " ms)");
            Experiment e = experiments[level];
            // Spawn the source
            activeSource = GameObject.Instantiate(sourceTemplate, bounds.transform);
            activeSource.transform.localPosition = e.sourceLocation;
            activeSource.transform.localScale = new Vector3(e.sourceDiameter, e.sourceDiameter, e.sourceDiameter);
            activeSource.name = "Source";
            // Spawn the target
            activeTarget = GameObject.Instantiate(targetTemplate, bounds.transform);
            activeTarget.transform.localPosition = e.targetLocation;
            activeTarget.transform.localScale = new Vector3(e.targetDiameter, e.targetDiameter, e.targetDiameter);
            activeTarget.name = "Target";
        }

        // If the trace is running, we write the transform to the log file
        if (tracing) {
            string line = Time.time.ToString("0.0000") + "; ";
            // Transform pointer location to local space of the bounding box [-1,1]^3 and then to [0,1]^3
            line += Experiment.toExperimentOrigin(bounds.transform.InverseTransformPoint(pointer.transform.position)).ToString() + ";";
            // line += buttonPressed().ToString() + ";";
            traceWriter.WriteLine(line);
        }

        // Check for intersection on button press
        if (buttonPressed()) {
            // If button is pressed on source, destroy it and start tracing
            if (activeSource != null && isColliding(activeSource)) {
                startTracing();
                Destroy(activeSource);
                activeSource = null;
            }
            // If source is already gone, and button is pressed on target, stop the trace
            if (activeSource == null && activeTarget != null && isColliding(activeTarget)) {
                stopTracing();
                Destroy(activeTarget);
                activeTarget = null;
                level += 1;
            }
        }

        // Add data to queue
        timeQueue.Enqueue(Time.time);
        flagQueue.Enqueue(poseFlags);
        rotationQueue.Enqueue(newRotation);
        positionQueue.Enqueue(newPosition);

        // Check if some delayed input is ready
        float nextTime = -1.0f;
        timeQueue.TryPeek(out nextTime);
        if (nextTime != -1.0f && Time.time - nextTime >= ((float) latencies[latencyIndex]) / 1000) {
            // If so, get data from queue
            timeQueue.Dequeue();
            PoseDataFlags flags = flagQueue.Dequeue();
            Quaternion rotation = rotationQueue.Dequeue();
            Vector3 position = positionQueue.Dequeue();
            // Standard motion tracker did this, i dont know why but whatever
            if ((flags & PoseDataFlags.Rotation) > 0) {
                transform.localRotation = rotation;
            }
            if ((flags & PoseDataFlags.Position) > 0) {
                transform.localPosition = position;
            }
        }

    }
}
