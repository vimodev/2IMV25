using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.SpatialTracking;
using System.IO;
using System;

class Experiment {
    public Vector3 sourceLocation;
    public float sourceDiameter;
    public Vector3 targetLocation;
    public float targetDiameter;
    public Experiment(float sx, float sy, float sz, float sd, float tx, float ty, float tz, float td) {
        this.sourceLocation = new Vector3(sx, sy, sz);
        this.sourceDiameter = sd;
        this.targetLocation = new Vector3(tx, ty, tz);
        this.targetDiameter = td;
    }
}

public class DelayedTrackedPoseDriver : TrackedPoseDriver {

    // Tracking specific game objects
    GameObject sourceTemplate;
    GameObject targetTemplate;
    GameObject activeSource;
    GameObject activeTarget;
    GameObject bounds;
    GameObject pointer;

    List<Experiment> experiments = new List<Experiment>(new Experiment[] {
        new Experiment(0.75f, -0.75f, -0.5f, 0.4f,
                        -0.75f, 0f, -0.75f, 0.2f),
        new Experiment(0.75f, -0.6f, -0.5f, 0.1f,
                        -0.75f, 0f, 0.5f, 0.5f)
    });
    int level;

    // Controller input stuff
    InputDevice controller;
    bool controllerSet;

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

    [SerializeField]
    int m_delay = 200;
    /// <summary>
    /// This determines the delay in the tracking input
    /// </summary>
    public int delay
    {
        get { return m_delay; }
        internal set { m_delay = value; }
    }

    // Start running a trace, and open a file write stream
    public void startTracing() {
        if (tracing) { stopTracing(); }
        traceFileName = DateTime.Now.ToString("yyyyMMddTHHmmssff") + "_trace_" + level.ToString() + ".txt";
        traceWriter = File.AppendText(traceFileName);
        traceWriter.WriteLine("Experiment: " + level.ToString());
        traceWriter.WriteLine("Latency: " + m_delay.ToString());
        traceWriter.WriteLine("Source: " + activeSource.transform.localPosition.ToString());
        traceWriter.WriteLine("SourceSize: " + activeSource.transform.localScale.x.ToString("0.0000"));
        traceWriter.WriteLine("Target: " + activeTarget.transform.localPosition.ToString());
        traceWriter.WriteLine("TargetSize: " + activeTarget.transform.localScale.x.ToString("0.0000"));
        traceWriter.WriteLine("");
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

    public void Start() {
        tracing = false;
        controllerSet = false;
        level = 0;
    }

    // On quit, if we are still tracing, stop
    void OnApplicationQuit() {
        stopTracing();
    }

    // Is the interaction button being pressed?
    public bool buttonPressed() {
        if (controllerSet == false) { controller = getController(); controllerSet = true; }
        bool buttonValue;
        if (controller.TryGetFeatureValue(CommonUsages.menuButton, out buttonValue) && buttonValue) {
            return true;
        }
        return false;
    }

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
        // If game objects are not set yet, set them
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
        // If active source and target are null, set them according to design
        if (activeSource == null && activeTarget == null && level < experiments.Count) {
            Debug.Log("Spawning source and target: level " + level.ToString());
            Experiment e = experiments[level];
            activeSource = GameObject.Instantiate(sourceTemplate, bounds.transform);
            activeSource.transform.localPosition = e.sourceLocation;
            activeSource.transform.localScale = new Vector3(e.sourceDiameter, e.sourceDiameter, e.sourceDiameter);
            activeSource.name = "Source";
            activeTarget = GameObject.Instantiate(targetTemplate, bounds.transform);
            activeTarget.transform.localPosition = e.targetLocation;
            activeTarget.transform.localScale = new Vector3(e.targetDiameter, e.targetDiameter, e.targetDiameter);
            activeTarget.name = "Target";
        }

        // If the trace is running, we write the transform to the log file
        if (tracing) {
            string line = Time.time.ToString("0.0000") + "; ";
            line += bounds.transform.InverseTransformPoint(pointer.transform.position).ToString() + "; ";
            line += buttonPressed().ToString() + ";";
            traceWriter.WriteLine(line);
        }

        // Check for intersection on button press
        if (buttonPressed()) {
            if (activeSource != null && isColliding(activeSource)) {
                startTracing();
                Destroy(activeSource);
                activeSource = null;
            }
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
        if (nextTime != -1.0f && Time.time - nextTime >= ((float) m_delay) / 1000) {
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
