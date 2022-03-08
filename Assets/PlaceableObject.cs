using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;


[Serializable]
public class PlacedObjects
{
    public string position { get; set; }
    public string rotation { get; set; }
    public string information { get; set; }
    public string id { get; set; }
    public override string ToString()
    {
        return base.ToString() + ": " + "position: "+ position + " " + "rotation: " + rotation + " " + "id: " + id;
    }
}

public class PlaceableObject : MonoBehaviour
{
    public string id;
    public string information;
    public string associatedCloudAnchor;
    public GameObject saveButton;
    public GameObject deleteButton;
    public GameObject editButton;
    public GameObject ParentAnchor;
    public TMP_Text informationTextField;
    public TMP_InputField informationTextFieldInput;
    public AnchorModuleScript parentCloudAnchor;
    public PlaceableObjectManager placeableObjectManager;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SaveObject()
    {
        parentCloudAnchor.UpdateObject(this);
    }

    public void EditData()
    {
        informationTextFieldInput.gameObject.SetActive(true);
        informationTextFieldInput.ActivateInputField();
        informationTextFieldInput.onSubmit.AddListener(FinishEditText);
    }

    public void FinishEditText(string text)
    {
        information = text;
        informationTextField.text = information;
        informationTextFieldInput.onSubmit.RemoveAllListeners();
        informationTextFieldInput.gameObject.SetActive(false);
        SaveObject();
    }

    public void Delete()
    {
        parentCloudAnchor.childObjects.Remove(this.id);
        parentCloudAnchor.placeableObjectManager.UpdateAnchor(parentCloudAnchor.currentAzureAnchorID, parentCloudAnchor);
        Debug.Log("Deleteing" + this.id);
        Debug.Log(parentCloudAnchor.childObjects.Count);
        Destroy(gameObject);

    }
}
