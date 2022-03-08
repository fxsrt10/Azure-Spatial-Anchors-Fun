using Microsoft.Azure.SpatialAnchors;
using Microsoft.Azure.SpatialAnchors.Unity;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

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
        anchorList.Add(anchor);
    }

    public void UpdateAnchor(string anchorID, AnchorModuleScript anchorGO)
    {
        Debug.Log($"{anchorID} is being updated");
        //if (anchorList == null)
        //{
        //    Debug.Log("Created anchorList for some reason");
        //    anchorList = new List<Anchors>();
        //    Anchors anchor = new Anchors();


        //    anchor.cloudAnchorID = anchorID;
        //    anchor.placedobjects = new List<PlacedObjects>();
        //    anchorList.Add(anchor);
        //    Debug.Log(anchorList.ToString());
        //}
        Anchors result = new Anchors();
        result.cloudAnchorID = anchorID;
        Debug.Log($"{anchorID} is being written to temp anchor");
        result.placedobjects = anchorGO.childObjects.Values.ToList();
        Debug.Log($"{anchorID} has placed objects written");
        if(anchorList.Count == 0)
        {
            anchorList.Add(result);
        }
        else
        {
            anchorList[0] = result;
        }
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
            //currentAnchor.
        }
#if !UNITY_EDITOR
        currentAnchor.FindAzureAnchor(anchorList[0].cloudAnchorID);
#endif

    }

    //    private void CloudManager_AnchorLocated(object sender, AnchorLocatedEventArgs args)
    //    {
    //        QueueOnUpdate(new Action(() => Debug.Log($"Anchor recognized as a possible Azure anchor")));

    //        if (args.Status == LocateAnchorStatus.Located || args.Status == LocateAnchorStatus.AlreadyTracked)
    //        {
    //            currentCloudAnchor = args.Anchor;

    //            QueueOnUpdate(() =>
    //            {
    //                Debug.Log($"Azure anchor located successfully");

    //                // Notify AnchorFeedbackScript
    //                OnASAAnchorLocated?.Invoke();

    //#if WINDOWS_UWP || UNITY_WSA
    //                // HoloLens: The position will be set based on the unityARUserAnchor that was located.

    //                // Create a local anchor at the location of the object in question
    //                gameObject.CreateNativeAnchor();

    //                // Notify AnchorFeedbackScript
    //                OnCreateLocalAnchor?.Invoke();

    //                // On HoloLens, if we do not have a cloudAnchor already, we will have already positioned the
    //                // object based on the passed in worldPos/worldRot and attached a new world anchor,
    //                // so we are ready to commit the anchor to the cloud if requested.
    //                // If we do have a cloudAnchor, we will use it's pointer to setup the world anchor,
    //                // which will position the object automatically.
    //                if (currentCloudAnchor != null)
    //                {
    //                    Debug.Log("Local anchor position successfully set to Azure anchor position");

    //                    //gameObject.GetComponent<UnityEngine.XR.WSA.WorldAnchor>().SetNativeSpatialAnchorPtr(currentCloudAnchor.LocalAnchor);

    //                    Pose anchorPose = Pose.identity;
    //                    anchorPose = currentCloudAnchor.GetPose();

    //                    Debug.Log($"Setting object to anchor pose with position '{anchorPose.position}' and rotation '{anchorPose.rotation}'");
    //                    transform.position = anchorPose.position;
    //                    transform.rotation = anchorPose.rotation;

    //                    removeAnchor = "Inactive";
    //                    // Create a native anchor at the location of the object in question
    //                    gameObject.CreateNativeAnchor();

    //                    // Notify AnchorFeedbackScript
    //                    OnCreateLocalAnchor?.Invoke();
    //                }

    //#elif UNITY_ANDROID || UNITY_IOS
    //                Pose anchorPose = Pose.identity;
    //                anchorPose = currentCloudAnchor.GetPose();

    //                Debug.Log($"Setting object to anchor pose with position '{anchorPose.position}' and rotation '{anchorPose.rotation}'");
    //                transform.position = anchorPose.position;
    //                transform.rotation = anchorPose.rotation;

    //                // Create a native anchor at the location of the object in question
    //                gameObject.CreateNativeAnchor();

    //                // Notify AnchorFeedbackScript
    //                OnCreateLocalAnchor?.Invoke();

    //#endif
    //            });
    //        }
    //        else
    //        {
    //            QueueOnUpdate(new Action(() => Debug.Log($"Attempt to locate Anchor with ID '{args.Identifier}' failed, locate anchor status was not 'Located' but '{args.Status}'")));
    //        }
    //    }
    //#endregion




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
