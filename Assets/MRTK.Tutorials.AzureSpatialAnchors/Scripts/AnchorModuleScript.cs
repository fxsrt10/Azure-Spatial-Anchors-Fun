using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Microsoft.Azure.SpatialAnchors;
using Microsoft.Azure.SpatialAnchors.Unity;

#if WINDOWS_UWP
using Windows.Storage;
#endif

public class Anchors
{
    public string cloudAnchorID { get; set; }
    public List<PlacedObjects> placedobjects { get; set; }

    public Anchors()
    {
        cloudAnchorID = "";
        placedobjects = new List<PlacedObjects>();
    }
    public override string ToString()
    {
        string easyList = JsonUtility.ToJson(placedobjects);
        return base.ToString() + ": " + cloudAnchorID.ToString() + easyList;
    }
}
public class AnchorModuleScript : MonoBehaviour
{
    [SerializeField]
    [Tooltip("The unique identifier used to identify the shared file (containing the Azure anchor ID) on the web server.")]
    private string publicSharingPin = "1982734901747";

    // Anchor ID for anchor stored in Azure (provided by Azure) 
    public string currentAzureAnchorID = "";

    public string removeAnchor = "Inactive";
    public Dictionary<string, PlacedObjects> childObjects;

    public SpatialAnchorManager cloudManager;
    public PlaceableObjectManager placeableObjectManager;
    public CloudSpatialAnchor currentCloudAnchor;
    private AnchorLocateCriteria anchorLocateCriteria;
    private CloudSpatialAnchorWatcher currentWatcher;
    public Action AnchorUpdated;


    private readonly Queue<Action> dispatchQueue = new Queue<Action>();

    #region Unity Lifecycle
    void Start()
    {
        // Get a reference to the SpatialAnchorManager component (must be on the same gameobject)
        //cloudManager = GetComponent<SpatialAnchorManager>();

        // Register for Azure Spatial Anchor events
    }

    public void Setup()
    {
        childObjects = new Dictionary<string, PlacedObjects>();

#if !UNITY_EDITOR
        cloudManager.AnchorLocated += CloudManager_AnchorLocated;
        anchorLocateCriteria = new AnchorLocateCriteria();
#endif

    }

    public void UpdateObject(PlaceableObject placeableObject)
    {
        childObjects[placeableObject.id].position = placeableObject.transform.position.ToString();
        childObjects[placeableObject.id].rotation = placeableObject.transform.rotation.eulerAngles.ToString();
        if (String.IsNullOrEmpty(placeableObject.information))
        {
            childObjects[placeableObject.id].information = "Untitled";
        }
        else
        {
            childObjects[placeableObject.id].information = placeableObject.information;
        }
        if(currentAzureAnchorID == "")
        {
            Debug.Log($"{ currentAzureAnchorID} is a generated anchorID for reasons");
            Guid guid = Guid.NewGuid();

            currentAzureAnchorID = guid.ToString();
           
        }
        placeableObjectManager.UpdateAnchor(currentAzureAnchorID, this);
    }

    public void SpawnChildObjects(List<PlacedObjects> childrenToSpawn, GameObject prefabTemplate, string cloudAnchorID)
    {
        foreach (var item in childrenToSpawn)
        {
            GameObject go = Instantiate(prefabTemplate, StringToVector3(item.position), Quaternion.Euler(StringToVector3(item.rotation)),this.transform);
            go.GetComponent<PlaceableObject>().associatedCloudAnchor = cloudAnchorID;
            go.GetComponent<PlaceableObject>().parentCloudAnchor = this;
            go.GetComponent<PlaceableObject>().id = item.id;
            if (String.IsNullOrEmpty(item.information))
            {
                item.information = "No Info Recorded";
                go.GetComponent<PlaceableObject>().informationTextField.text = "No Info Recorded";
            }
            else
            {
                go.GetComponent<PlaceableObject>().information = item.information;
                go.GetComponent<PlaceableObject>().informationTextField.text = item.information;
            }
            this.childObjects.Add(item.id, item);
        }
    }

    public void AddNewPlacedObject(GameObject newPlacedObjectGO)
    {
        Guid guid = Guid.NewGuid();
        PlacedObjects newPlacedObject = new PlacedObjects();
        newPlacedObject.id = guid.ToString();
        newPlacedObjectGO.GetComponent<PlaceableObject>().id = guid.ToString();
        newPlacedObject.position = newPlacedObjectGO.transform.position.ToString();
        newPlacedObject.rotation = newPlacedObjectGO.transform.rotation.eulerAngles.ToString();
        childObjects.Add(newPlacedObject.id, newPlacedObject);
    }

    void Update()
    {
        lock (dispatchQueue)
        {
            if (dispatchQueue.Count > 0)
            {
                dispatchQueue.Dequeue()();
            }
        }
    }

    void OnDestroy()
    {
        //if (cloudManager != null && cloudManager.Session != null)
        //{
        //    cloudManager.DestroySession();
        //}

        if (currentWatcher != null)
        {
            currentWatcher.Stop();
            currentWatcher = null;
        }
    }
    #endregion

    public static Vector3 StringToVector3(string sVector)
    {
        // Remove the parentheses
        if (sVector.StartsWith("(") && sVector.EndsWith(")"))
        {
            sVector = sVector.Substring(1, sVector.Length - 2);
        }

        // split the items
        string[] sArray = sVector.Split(',');

        // store as a Vector3
        Vector3 result = new Vector3(
            float.Parse(sArray[0]),
            float.Parse(sArray[1]),
            float.Parse(sArray[2]));

        return result;
    }

    public async void CreateAzureAnchor(GameObject theObject)
    {
        Debug.Log("\nAnchorModuleScript.CreateAzureAnchor()");
        removeAnchor = "Active";

        // Notify AnchorFeedbackScript
        OnCreateAnchorStarted?.Invoke();

        // First we create a native XR anchor at the location of the object in question
        gameObject.CreateNativeAnchor();

        // Notify AnchorFeedbackScript
        OnCreateLocalAnchor?.Invoke();

        await Task.Delay(1000);

        // Then we create a new local cloud anchor
        CloudSpatialAnchor localCloudAnchor = new CloudSpatialAnchor();

        // Now we set the local cloud anchor's position to the native XR anchor's position
        localCloudAnchor.LocalAnchor =await theObject.FindNativeAnchor().GetPointer();

        // Check to see if we got the local XR anchor pointer
        if (localCloudAnchor.LocalAnchor == IntPtr.Zero)
        {
            Debug.Log("Didn't get the local anchor...");
            return;
        }
        else
        {
            Debug.Log("Local anchor created");
        }

        // In this sample app we delete the cloud anchor explicitly, but here we show how to set an anchor to expire automatically
        localCloudAnchor.Expiration = DateTimeOffset.Now.AddDays(7);

        // Save anchor to cloud
        while (!cloudManager.IsReadyForCreate)
        {
            await Task.Delay(330);
            float createProgress = cloudManager.SessionStatus.RecommendedForCreateProgress;
            QueueOnUpdate(new Action(() => Debug.Log($"Move your device to capture more environment data: {createProgress:0%}")));
        }

        bool success;

        try
        {
            Debug.Log("Creating Azure anchor... please wait...");

            // Actually save
            await cloudManager.CreateAnchorAsync(localCloudAnchor);

            // Store
            currentCloudAnchor = localCloudAnchor;
            localCloudAnchor = null;

            // Success?
            success = currentCloudAnchor != null;

            if (success)
            {
                Debug.Log($"Azure anchor with ID '{currentCloudAnchor.Identifier}' created successfully");

                // Notify AnchorFeedbackScript
                OnCreateAnchorSucceeded?.Invoke();

                // Update the current Azure anchor ID
                Debug.Log($"Current Azure anchor ID updated to '{currentCloudAnchor.Identifier}'");
                currentAzureAnchorID = currentCloudAnchor.Identifier;
            }
            else
            {
                Debug.Log($"Failed to save cloud anchor with ID '{currentAzureAnchorID}' to Azure");

                // Notify AnchorFeedbackScript
                OnCreateAnchorFailed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Debug.Log(ex.ToString());
        }
    }

    public void RemoveLocalAnchor(GameObject theObject)
    {
#if !(UNITY_ANDROID || UNITY_IOS)
        Debug.Log("\nAnchorModuleScript.RemoveLocalAnchor()");
        // Notify AnchorFeedbackScript
        //OnRemoveLocalAnchor?.Invoke();
        if (removeAnchor == "Inactive")
        {
            gameObject.DeleteNativeAnchor();

            if (gameObject.FindNativeAnchor() == null)
            {
                Debug.Log("Local anchor deleted succesfully");
            }
            else
            {
                Debug.Log("Attempt to delete local anchor failed");
            }
        }
        else
        {
            Debug.Log("Local anchor deleted succesfully");
        }
#else

 Debug.Log("\nAnchorModuleScript.RemoveLocalAnchor()");

#endif
    }

    public void FindAzureAnchor(string id = "")
    {
        Debug.Log("\nAnchorModuleScript.FindAzureAnchor()");

        if (id != "")
        {
            currentAzureAnchorID = id;
        }

        // Notify AnchorFeedbackScript
        OnFindASAAnchor?.Invoke();

        // Set up list of anchor IDs to locate
        List<string> anchorsToFind = new List<string>();

        if (currentAzureAnchorID != "")
        {
            anchorsToFind.Add(currentAzureAnchorID);
        }
        else
        {
            Debug.Log("Current Azure anchor ID is empty");
            return;
        }

        anchorLocateCriteria.Identifiers = anchorsToFind.ToArray();
        Debug.Log($"Anchor locate criteria configured to look for Azure anchor with ID '{currentAzureAnchorID}'");

        // Start watching for Anchors
        if ((cloudManager != null) && (cloudManager.Session != null))
        {
            currentWatcher = cloudManager.Session.CreateWatcher(anchorLocateCriteria);
            Debug.Log("Watcher created");
            Debug.Log("Looking for Azure anchor... please wait...");
        }
        else
        {
            Debug.Log("Attempt to create watcher failed, no session exists");
            currentWatcher = null;
        }
    }

    public async void DeleteAzureAnchor()
    {
        Debug.Log("\nAnchorModuleScript.DeleteAzureAnchor()");

        // Notify AnchorFeedbackScript
        OnDeleteASAAnchor?.Invoke();

        // Delete the Azure anchor with the ID specified off the server and locally
        await cloudManager.DeleteAnchorAsync(currentCloudAnchor);
        currentCloudAnchor = null;

        Debug.Log("Azure anchor deleted successfully");
    }

    public void SaveAzureAnchorIdToDisk()
    {
        Debug.Log("\nAnchorModuleScript.SaveAzureAnchorIDToDisk()");

        string filename = "SavedAzureAnchorID.txt";
        string path = Application.persistentDataPath;

#if WINDOWS_UWP
        StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
        path = storageFolder.Path.Replace('\\', '/') + "/";
#endif

        string filePath = Path.Combine(path, filename);
        File.WriteAllText(filePath, currentAzureAnchorID);

        Debug.Log($"Current Azure anchor ID '{currentAzureAnchorID}' successfully saved to path '{filePath}'");
    }

    public void GetAzureAnchorIdFromDisk()
    {
        Debug.Log("\nAnchorModuleScript.LoadAzureAnchorIDFromDisk()");

        string filename = "SavedAzureAnchorID.txt";
        string path = Application.persistentDataPath;

#if WINDOWS_UWP
        StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
        path = storageFolder.Path.Replace('\\', '/') + "/";
#endif

        string filePath = Path.Combine(path, filename);
        currentAzureAnchorID = File.ReadAllText(filePath);

        Debug.Log($"Current Azure anchor ID successfully updated with saved Azure anchor ID '{currentAzureAnchorID}' from path '{path}'");
    }

    public void ShareAzureAnchorIdToNetwork()
    {
        Debug.Log("\nAnchorModuleScript.ShareAzureAnchorID()");

        StartCoroutine(ShareAzureAnchorIdToNetworkCoroutine());
    }

    public void GetAzureAnchorIdFromNetwork()
    {
        Debug.Log("\nAnchorModuleScript.GetSharedAzureAnchorID()");

        StartCoroutine(GetSharedAzureAnchorIDCoroutine(publicSharingPin));
    }

    #region Event Handlers
    private void CloudManager_AnchorLocated(object sender, AnchorLocatedEventArgs args)
    {
        QueueOnUpdate(new Action(() => Debug.Log($"Anchor recognized as a possible Azure anchor")));

        if (args.Status == LocateAnchorStatus.Located || args.Status == LocateAnchorStatus.AlreadyTracked)
        {
            currentCloudAnchor = args.Anchor;

            QueueOnUpdate(() =>
            {
                Debug.Log($"Azure anchor located successfully");

                // Notify AnchorFeedbackScript
                OnASAAnchorLocated?.Invoke();

#if WINDOWS_UWP || UNITY_WSA
                // HoloLens: The position will be set based on the unityARUserAnchor that was located.

                // Create a local anchor at the location of the object in question
                gameObject.CreateNativeAnchor();

                // Notify AnchorFeedbackScript
                OnCreateLocalAnchor?.Invoke();

                // On HoloLens, if we do not have a cloudAnchor already, we will have already positioned the
                // object based on the passed in worldPos/worldRot and attached a new world anchor,
                // so we are ready to commit the anchor to the cloud if requested.
                // If we do have a cloudAnchor, we will use it's pointer to setup the world anchor,
                // which will position the object automatically.
                if (currentCloudAnchor != null)
                {
                    Debug.Log("Local anchor position successfully set to Azure anchor position");

                    //gameObject.GetComponent<UnityEngine.XR.WSA.WorldAnchor>().SetNativeSpatialAnchorPtr(currentCloudAnchor.LocalAnchor);

                    Pose anchorPose = Pose.identity;
                    anchorPose = currentCloudAnchor.GetPose();

                    Debug.Log($"Setting object to anchor pose with position '{anchorPose.position}' and rotation '{anchorPose.rotation}'");
                    transform.position = anchorPose.position;
                    transform.rotation = anchorPose.rotation;

                    removeAnchor = "Inactive";
                    // Create a native anchor at the location of the object in question
                    gameObject.CreateNativeAnchor();

                    // Notify AnchorFeedbackScript
                    OnCreateLocalAnchor?.Invoke();
                }

#elif UNITY_ANDROID || UNITY_IOS
                Pose anchorPose = Pose.identity;
                anchorPose = currentCloudAnchor.GetPose();

                Debug.Log($"Setting object to anchor pose with position '{anchorPose.position}' and rotation '{anchorPose.rotation}'");
                transform.position = anchorPose.position;
                transform.rotation = anchorPose.rotation;

                // Create a native anchor at the location of the object in question
                gameObject.CreateNativeAnchor();

                // Notify AnchorFeedbackScript
                OnCreateLocalAnchor?.Invoke();

#endif
            });
        }
        else
        {
            QueueOnUpdate(new Action(() => Debug.Log($"Attempt to locate Anchor with ID '{args.Identifier}' failed, locate anchor status was not 'Located' but '{args.Status}'")));
        }
    }
    #endregion

    #region Internal Methods and Coroutines
    private void QueueOnUpdate(Action updateAction)
    {
        lock (dispatchQueue)
        {
            dispatchQueue.Enqueue(updateAction);
        }
    }

    IEnumerator ShareAzureAnchorIdToNetworkCoroutine()
    {
        string filename = "SharedAzureAnchorID." + publicSharingPin;
        string path = Application.persistentDataPath;

#if WINDOWS_UWP
        StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
        path = storageFolder.Path + "/";           
#endif

        string filePath = Path.Combine(path, filename);
        File.WriteAllText(filePath, currentAzureAnchorID);

        Debug.Log($"Current Azure anchor ID '{currentAzureAnchorID}' successfully saved to path '{filePath}'");
        Byte[] binaryData = File.ReadAllBytes(filePath);

        WWWForm form = new WWWForm();
        form.AddBinaryData("the_file", binaryData, Path.GetFileName(filePath), "text/plain");
        form.AddField("replace_file", 1);

        UnityWebRequest www = UnityWebRequest.Post("http://167.99.111.15:8090/uploadFile.php", form);
        yield return www.SendWebRequest();

        if (www.isHttpError || www.isNetworkError)
        {
            Debug.Log(www.error);
        }
        else
        {
            Debug.Log($"Current Azure anchor ID '{currentAzureAnchorID}' shared successfully");
        }
    }


    IEnumerator GetSharedAzureAnchorIDCoroutine(string sharingPin)
    {
        string url = "http://167.99.111.15:8090/file-uploads/static/file." + sharingPin.ToLower();

        Debug.Log($"Looking for url '{url}'... please wait...");

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();
            if (www.isNetworkError || www.isHttpError)
            {
                Debug.Log(www.error);
            }
            else
            {
                Debug.Log("Downloading... please wait...");

                string filename = "SharedAzureAnchorID." + publicSharingPin;
                string path = Application.persistentDataPath;

#if WINDOWS_UWP
                StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
                path = storageFolder.Path;
#endif
                currentAzureAnchorID = www.downloadHandler.text;

                Debug.Log($"Current Azure anchor ID successfully updated with shared Azure anchor ID '{currentAzureAnchorID}' url");

                string filePath = Path.Combine(path, filename);
                File.WriteAllText(filePath, currentAzureAnchorID);
            }
        }
    }
    #endregion

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
