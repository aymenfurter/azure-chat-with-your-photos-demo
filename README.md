# Start a conversation with your Photos
<img style="border-radius: 10px;" src="architecture.png?raw=true">

This example demonstrates how to create a ChatGPT-style application using the Retrieval Augmented Generation (RAG). It leverages Azure's OpenAI Service to access the ChatGPT model (gpt-3.5-turbo) and employs Azure Cognitive Search Vector Search for data indexing and retrieval.

## Features
The chatbot can analyze and comprehend the details of your uploaded images. This enables you to interact with the chatbot as if you were conversing with someone who has viewed the same photos. For instance, you could inquire, "Hello, have you seen a picture of a dog with a frisbee in the snow?"

<img style="border-radius: 10px;" src="screenshot.png?raw=true">

## Prerequisites 
To deploy the application, please ensure that you have the following dependencies installed on your machine.
* [Azure Developer CLI](https://aka.ms/azure-dev/install)
* [Python 3.9+](https://www.python.org/downloads/)
* [Node.js 14+](https://nodejs.org/en/download/)
* [Git](https://git-scm.com/downloads)
* [Bash / WSL](https://learn.microsoft.com/en-us/windows/wsl/install) 

## Installation
### CAUTION
- This repository is still in early development.
- Your photos are currently stored in a public Azure Blob Storage container. Please do not upload any sensitive data.
- This repository uses LLaVA which is a research project. Conducting thorough due diligence to determine its suitability for your project.

## Setup procedure
1. Clone the repository and navigate to the project root
2. Run `azd auth login`
3. Put the photos you want to index in the `./data` folder
4. Run `azd up` 
5. You will be prompted to enter the LLAVA API URL. For functionality of the app, you must provide an active Llava instance URL which will generate descriptions for images. Note that the app is designed to integrate with the gradio endpoint. Thus, a target API might look like: `wss://path-to-your-instance.tld/queue/join`.

The current indexing process is suboptimal. During my evaluation, the system could only handle an approximate rate of 300 images hourly.

## Future Improvements
- [ ] Improve indexing performance
- [ ] Ability to query based on image metadata (e.g. location)
- [ ] Bot Framework Support (In progress)
- [ ] Reduce Hallucinations / Prompt tweaks

## Troubleshooting
- To upload images to the Azure Blob Storage container, review that your user account has the Storage Blob Data Contributor role assigned.
