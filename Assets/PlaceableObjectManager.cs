using Microsoft.Azure.SpatialAnchors;
using Microsoft.Azure.SpatialAnchors.Unity;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

#if WINDOWS_UWP
using Windows.Storage;
#endif

public class PlaceableObjectManager : MonoBehaviour
{
    public GameObject anchorPrefab;
    public GameObject placeableObjectPrefab;
    private SpatialAnchorManager spatialAnchorManager;
    private AnchorLocateCriteria anchorLocateCriteria;
    public List<AnchorModuleScript> anchorsGOList;
    public AnchorModuleScript currentAnchor;
    public List<Anchors> anchorList;
    private CloudSpatialAnchorWatcher currentWatcher;

    // Start is called before the first frame update
    void Start()
    {
        spatialAnchorManager = GetComponent<SpatialAnchorManager>();
        anchorLocateCriteria = new AnchorLocateCriteria();
        anchorList = new List<Anchors>();
        anchorsGOList = new List<AnchorModuleScript>();
        Invoke("StartAzureSession", 2.0f);
        //StartAzureSession();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SpawnNewAnchor()
    {
        GameObject anchorObject = Instantiate(anchorPrefab, transform.position + transform.forward * .5f, transform.rotation);
        currentAnchor = anchorObject.GetComponent<AnchorModuleScript>();
        currentAnchor.cloudManager = spatialAnchorManager;
        currentAnchor.placeableObjectManager = this;
        anchorsGOList.Add(currentAnchor);
        currentAnchor.Setup();
        currentAnchor.CreateAzureAnchor(currentAnchor.gameObject);
        currentAnchor.OnCreateAnchorSucceeded += CurrentAnchor_OnCreateAnchorSucceeded;
        
    }

    private void CurrentAnchor_OnCreateAnchorSucceeded()
    {
        Debug.Log("Created anchorList");
        Anchors anchor = new Anchors();
        anchor.cloudAnchorID = currentAnchor.currentAzureAnchorID;
        anchor.placedobjects = new List<PlacedObjects>();
        anchor.placedobjects = currentAnchor.childObjects.Values.ToList();
        Debug.Log("and added new anchor");
    }

    public void UpdateAnchor(string anchorID, AnchorModuleScript anchorGO)
    {
        Debug.Log($"{anchorID} is being updated");

#if UNITY_EDITOR
        if (String.IsNullOrEmpty(anchorID))
        {
            if (String.IsNullOrEmpty(anchorID)) {
                anchorID = "123-editor-anchor-id";
            }
        }
#endif
        Anchors result = new Anchors();
        result.cloudAnchorID = anchorID;
        result.placedobjects = anchorGO.childObjects.Values.ToList();
        Debug.Log($"{anchorID} has placed objects written");
        anchorList = new List<Anchors>();
        Debug.Log(anchorList.Count);
        anchorList.Add(result);
    }

    public void CreateNewPlaceableObject()
    {
        GameObject newPlaceableObject = Instantiate(placeableObjectPrefab, currentAnchor.gameObject.transform);
        newPlaceableObject.transform.localPosition = Vector3.zero;
        newPlaceableObject.GetComponent<PlaceableObject>().parentCloudAnchor = currentAnchor;
        currentAnchor.AddNewPlacedObject(newPlaceableObject);
    }

    public async void StartAzureSession()
    {
        Debug.Log("\nAnchorModuleScript.StartAzureSession()");

        // Notify AnchorFeedbackScript
        OnStartASASession?.Invoke();

        Debug.Log("Starting Azure session... please wait...");

        if (spatialAnchorManager.Session == null)
        {
            // Creates a new session if one does not exist
            await spatialAnchorManager.CreateSessionAsync();
        }

        // Starts the session if not already started
        await spatialAnchorManager.StartSessionAsync();

        Debug.Log("Azure session started successfully");
    }

    public async void StopAzureSession()
    {
        Debug.Log("\nAnchorModuleScript.StopAzureSession()");

        // Notify AnchorFeedbackScript
        OnEndASASession?.Invoke();

        Debug.Log("Stopping Azure session... please wait...");

        // Stops any existing session
        spatialAnchorManager.StopSession();

        // Resets the current session if there is one, and waits for any active queries to be stopped
        await spatialAnchorManager.ResetSessionAsync();

        Debug.Log("Azure session stopped successfully");
    }

    public async void DeleteAnchor()
    {
        Debug.Log("\nAnchorModuleScript.DeleteAzureAnchor()");

        // Notify AnchorFeedbackScript
        OnDeleteASAAnchor?.Invoke();

        // Delete the Azure anchor with the ID specified off the server and locally
        await spatialAnchorManager.DeleteAnchorAsync(currentAnchor.currentCloudAnchor);
        GameObject.Destroy(currentAnchor);
        currentAnchor = null;

        Debug.Log("Azure anchor deleted successfully");
    }

    public void SaveSpaceToDevice()
    {
        Debug.Log("\nPlaceableObjectManager.SaveSpaceToDevice()");

        string filename = "SavedAzureAnchorID.txt";
        string path = Application.persistentDataPath;

#if WINDOWS_UWP
        StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
        path = storageFolder.Path.Replace('\\', '/') + "/";

        
#endif
        string json = JsonConvert.SerializeObject(anchorList, Formatting.Indented);
        string filePath = Path.Combine(path, filename);
        
        File.WriteAllText(filePath, json);

        //Debug.Log($"Current Azure anchor ID '{currentAnchor.currentAzureAnchorID}' successfully saved to path '{filePath}'");
        Debug.Log(json);
    }

    public void FindAzureAnchor(string id = "")
    {
        Debug.Log("\nPlaceableObjectManager.FindAzureAnchor()");

        if (id != "")
        {
            currentAnchor.currentAzureAnchorID = id;
        }

        // Notify AnchorFeedbackScript
        OnFindASAAnchor?.Invoke();

        // Set up list of anchor IDs to locate
        List<string> anchorsToFind = new List<string>();

        if (currentAnchor.currentAzureAnchorID != "")
        {
            anchorsToFind.Add(currentAnchor.currentAzureAnchorID);
        }
        else
        {
            Debug.Log("Current Azure anchor ID is empty");
            return;
        }

        anchorLocateCriteria.Identifiers = anchorsToFind.ToArray();
        Debug.Log($"Anchor locate criteria configured to look for Azure anchor with ID '{currentAnchor.currentAzureAnchorID}'");

        // Start watching for Anchors
        if ((spatialAnchorManager != null) && (spatialAnchorManager.Session != null))
        {
            currentWatcher = spatialAnchorManager.Session.CreateWatcher(anchorLocateCriteria);
            Debug.Log("Watcher created");
            Debug.Log("Looking for Azure anchor... please wait...");
        }
        else
        {
            Debug.Log("Attempt to create watcher failed, no session exists");
            currentWatcher = null;
        }
    }

    public void GetAzureAnchorsFromDisk()
    {
        Debug.Log("\nPlaceableObjectManager.LoadAzureAnchorIDFromDisk()");

        string filename = "SavedAzureAnchorID.txt";
        string path = Application.persistentDataPath;

#if WINDOWS_UWP
        StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
        path = storageFolder.Path.Replace('\\', '/') + "/";
#endif

        string filePath = Path.Combine(path, filename);
        string jsonBlobText = File.ReadAllText(filePath);
        anchorList = JsonConvert.DeserializeObject<List<Anchors>>(jsonBlobText);
        foreach (var item in anchorList[0].placedobjects)
        {
            Debug.Log(item.ToString());
        }
        if(currentAnchor == null)
        {
            GameObject anchorObject = Instantiate(anchorPrefab, transform.position + transform.forward * .5f, transform.rotation);
            currentAnchor = anchorObject.GetComponent<AnchorModuleScript>();
            currentAnchor.cloudManager = spatialAnchorManager;
            currentAnchor.placeableObjectManager = this;
            anchorsGOList.Add(currentAnchor);
            currentAnchor.Setup();
            currentAnchor.SpawnChildObjects(anchorList[0].placedobjects, placeableObjectPrefab, anchorList[0].cloudAnchorID);
        }
#if !UNITY_EDITOR
        currentAnchor.FindAzureAnchor(anchorList[0].cloudAnchorID);
#endif

    }




    #region Public Events
    public delegate void StartASASessionDelegate();
    public event StartASASessionDelegate OnStartASASession;

    public delegate void EndASASessionDelegate();
    public event EndASASessionDelegate OnEndASASession;

    public delegate void CreateAnchorDelegate();
    public event CreateAnchorDelegate OnCreateAnchorStarted;
    public event CreateAnchorDelegate OnCreateAnchorSucceeded;
    public event CreateAnchorDelegate OnCreateAnchorFailed;

    public delegate void CreateLocalAnchorDelegate();
    public event CreateLocalAnchorDelegate OnCreateLocalAnchor;

    public delegate void RemoveLocalAnchorDelegate();
    public event RemoveLocalAnchorDelegate OnRemoveLocalAnchor;

    public delegate void FindAnchorDelegate();
    public event FindAnchorDelegate OnFindASAAnchor;

    public delegate void AnchorLocatedDelegate();
    public event AnchorLocatedDelegate OnASAAnchorLocated;

    public delegate void DeleteASAAnchorDelegate();
    public event DeleteASAAnchorDelegate OnDeleteASAAnchor;
    #endregion
}
