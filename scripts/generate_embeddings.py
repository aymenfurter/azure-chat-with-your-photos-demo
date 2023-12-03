import os
import hashlib
import requests
import json
import base64
import random
import string
import glob
from collections import defaultdict
from datetime import datetime
from PIL import Image
from PIL.ExifTags import TAGS, GPSTAGS
import websocket
from azure.identity import AzureDeveloperCliCredential
from azure.core.credentials import AzureKeyCredential
from azure.storage.blob import BlobServiceClient
from azure.search.documents.indexes import SearchIndexClient
from azure.search.documents.indexes.models import SearchIndex, SimpleField, SearchableField, SearchField, SearchFieldDataType, SemanticSettings, SemanticConfiguration, PrioritizedFields, VectorSearch, VectorSearchAlgorithmConfiguration, HnswParameters
from azure.search.documents import SearchClient
from azure.search.documents.indexes.models import *
import openai
import shutil

STRING_LENGTH = 10


def get_geotagging(exif):
    if not exif:
        raise ValueError("No EXIF metadata found")

    geotagging = {}
    for (idx, tag) in TAGS.items():
        if tag == 'GPSInfo':
            if idx not in exif:
                raise ValueError("No EXIF geotagging found")

            for (key, val) in GPSTAGS.items():
                if key in exif[idx]:
                    geotagging[val] = exif[idx][key]

    return geotagging


def get_decimal_from_dms(dms, ref):
    degrees = dms[0].numerator / dms[0].denominator
    minutes = dms[1].numerator / dms[1].denominator / 60.0
    seconds = dms[2].numerator / dms[2].denominator / 3600.0

    if ref in ['S', 'W']:
        degrees = -degrees
        minutes = -minutes
        seconds = -seconds

    return round(degrees + minutes + seconds, 5)


def get_coordinates(geotags):
    lat = get_decimal_from_dms(geotags['GPSLatitude'], geotags['GPSLatitudeRef'])
    lon = get_decimal_from_dms(geotags['GPSLongitude'], geotags['GPSLongitudeRef'])
    return (lat, lon)


def get_location_string_from_coords(lat, lon):
    try:
        response = requests.get(f"https://nominatim.openstreetmap.org/reverse?lat={lat}&lon={lon}&format=json")
        data = response.json()
        return data["display_name"]
    except:
        return "Location not found"


def get_location_from_image(filename):
    try:
        image = Image.open(filename)
        exif = image._getexif()
        geotags = get_geotagging(exif)
        lat, lon = get_coordinates(geotags)
        return get_location_string_from_coords(lat, lon)
    except Exception as e:
        return None


def get_date_from_image(filename):
    try:
        image = Image.open(filename)
        exif_data = image._getexif()

        if exif_data is None:
            raise ValueError("No EXIF data found")

        for (tag, value) in exif_data.items():
            tag_name = TAGS.get(tag, tag)
            if tag_name == "DateTime":
                date_str = value.split()[0].replace(":", "-")
                return datetime.strptime(date_str, '%Y-%m-%d')

        return None
    except Exception as e:
        return None

def encode_image_to_base64(image_path):
    with open(image_path, "rb") as image_file:
        return base64.b64encode(image_file.read()).decode('utf-8')

def process_image(image_path):
    base64_image = encode_image_to_base64(image_path)
    response = openai.ChatCompletion.create(
    model="gpt-4-vision-preview",
    messages=[
            {
            "role": "user",
            "content": [
                {
                "type": "text",
                "text": "Describe the image in as much detail as possible. 200 words or more.",
                },
                {
                    "type": "image_url",
                    "image_url": {
                        "url": f"data:image/jpeg;base64,{base64_image}",
                    },
                }
            ],
            }
        ],
        max_tokens=300,
    )
    
    return response.choices[0].message.content


def initialize_search_index(acs_key, acs_instance):
    auth_credentials = AzureKeyCredential(acs_key)
    search_client = SearchIndexClient(endpoint=f"https://{acs_instance}.search.windows.net/",
                                      credential=auth_credentials)

    if "embeddings" not in search_client.list_index_names():
        index_structure = SearchIndex(
            name="embeddings",
            fields=[
                SimpleField(name="Id", type="Edm.String", key=True),
                SearchableField(name="Text", type="Edm.String", analyzer_name="en.microsoft"),
                SearchableField(name="Description", type="Edm.String", analyzer_name="en.microsoft"),
                SearchableField(name="AdditionalMetadata", type="Edm.String", analyzer_name="en.microsoft"),
                SearchableField(name="ExternalSourceName", type="Edm.String", analyzer_name="en.microsoft"),
                SimpleField(name="CreatedAt", type=SearchFieldDataType.DateTimeOffset, filterable=True, sortable=True),
                SearchableField(name="URL", type="Edm.String", analyzer_name="en.microsoft"),
                SearchableField(name="Location", type="Edm.String", analyzer_name="en.microsoft"),
                SearchField(name="Vector", type=SearchFieldDataType.Collection(SearchFieldDataType.Single),
                            hidden=False, searchable=True, filterable=False, sortable=False, facetable=False,
                            dimensions=1536, vector_search_configuration="default"),
            ],
            semantic_settings=SemanticSettings(
                configurations=[SemanticConfiguration(
                    name='standard',
                    prioritized_fields=PrioritizedFields(
                        title_field=None, prioritized_content_fields=[SemanticField(field_name='Text')]))]),
                vector_search=VectorSearch(
                    algorithm_configurations=[
                        VectorSearchAlgorithmConfiguration(
                            name="default",
                            kind="hnsw",
                            hnsw_parameters=HnswParameters(metric="cosine")
                        )
                    ]
                )
        )
        print("Initializing search index")
        search_client.create_index(index=index_structure)
    else:
        print("Search index already exists")


def create_embeddings(storage_account, limit=5):
    glob_paths = glob.glob("data/*.jpg")[:limit]

    for glob_path in glob_paths:
        destination = os.path.join("data_completed", os.path.basename(glob_path))
        shutil.move(glob_path, destination)

    blob_service_client = BlobServiceClient(
        account_url=f"https://{storage_account}.blob.core.windows.net",
        credential=AzureDeveloperCliCredential()
    )
    container_name = "images"

    if not blob_service_client.get_container_client("images").exists():
        blob_service_client.create_container(container_name)

    embeddings = defaultdict(list)

    for dglob_path in glob_paths:
        glob_path = os.path.join("data_completed", os.path.basename(dglob_path))
        print (f"Processing.. {glob_path}")
        blob_client = blob_service_client.get_blob_client(container=container_name, blob=glob_path)
        print (f"Uploading to Blob Storage.. {glob_path}")
        blob_client.upload_blob(open(glob_path, "rb"), overwrite=True)
        print (f"Generating Image Description.. {glob_path}")
        text = process_image(glob_path)
        location = get_location_from_image(glob_path)
        date = get_date_from_image(glob_path)
        content = f"URL: {blob_client.url}\nImage description: {text}\nLocation: {location}\nDate: {date}"
        print (f"Producing Embedding.. {glob_path}")
        embeddings[glob_path] = {
            "Id": hashlib.md5(glob_path.encode()).hexdigest(),
            "Text": content,
            "Description": "",
            "AdditionalMetadata": "",
            "ExternalSourceName": "Custom",
            "CreatedAt": date,
            "URL": blob_client.url,
            "Location": location,
            "Vector": openai.Embedding.create(model="text-embedding-ada-002", input=text)["data"][0]["embedding"]
        }
    
    return embeddings


def upload_file_to_blob_storage(file_path, storage_account_name):
    blob_service_client = BlobServiceClient.from_connection_string(storage_account_name)
    blob_client = blob_service_client.get_blob_client(container="images", blob=file_path)
    with open(file_path, "rb") as data:
        blob_client.upload_blob(data)


def index(embedding_data, acs_key, acs_instance, batch_size=5):
    auth_credentials = AzureKeyCredential(acs_key)
    search_client = SearchClient(endpoint=f"https://{acs_instance}.search.windows.net/",
                                 index_name="embeddings",
                                 credential=auth_credentials)
    batch = []
    for item in embedding_data.values():
        batch.append(item)
        if len(batch) >= batch_size:
            search_client.upload_documents(documents=batch)
            report_status(search_client, batch)
            batch = []

    if batch:
        search_client.upload_documents(documents=batch)
        report_status(search_client, batch)


def report_status(search_client, batch):
    try:
        results = search_client.upload_documents(batch)
        for result in results:
            print("Uploaded {} with Key {}".format(result.status_code, result.key))
    except Exception as e:
        print("Error:", e)


if __name__ == "__main__":
    if not os.path.exists("data_completed"):
        os.makedirs("data_completed")
    storage_account = os.environ.get('AZURE_STORAGE_ACCOUNT')
    gpt4v_key = os.environ.get('OPENAI_KEY')
    acs_key = os.environ.get('ACS_KEY')
    acs_instance = os.environ.get('ACS_INSTANCE')

    if not gpt4v_key:
        raise ValueError("OPENAI_KEY environment variable is not set. Until GPT-4 Vision is available on Azure, you need to provide an OpenAI API key.")

    openai.api_key = gpt4v_key
    # openai.api_type = "azure"
    # openai.api_key = os.environ.get('AZURE_OPENAI_API_KEY')
    # openai.api_base = os.environ.get('AZURE_OPENAI_ENDPOINT')
    # openai.api_version = "2022-12-01"

    initialize_search_index(acs_key, acs_instance)
    while glob.glob("data/*.jpg"):
        try:
            embeddings = create_embeddings(storage_account)
            index(embeddings, acs_key=acs_key, acs_instance=acs_instance)
        except Exception as e:
            print(f"WARNING: Error encountered: {e}")
            continue
