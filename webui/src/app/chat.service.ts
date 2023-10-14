import { Injectable } from '@angular/core';
import { environment  } from 'src/environments/environment';
const API_URL = environment.targetUrl + 'chat';

interface Variable {
  key: string;
  value: string;
}

@Injectable({
  providedIn: 'root',
})
export class ChatService {
  constructor() {}

  async sendMessage(
    userMessage: string,
    messages: { role: string; content: string; pictures?: string[] }[]
  ): Promise<{ messages: { role: string; content: string; pictures?: string[] }[]; response: string }> {

    try {
      // Construct the 'History' string
      const history = messages
        .filter((msg) => msg.role === 'user')
        .map((msg) => msg.content)
        .join('\n');

      const response = await fetch(API_URL, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ 
          Input: userMessage, 
          Variables: [
            { Key: 'History', Value: history },
          ] 
        }),
      });

      if (!response.ok) {
        throw new Error(`API request failed: ${response.statusText}`);
      }

      messages.push({ role: 'user', content: userMessage });
      const data = await response.json();
      const pictureUrls = data.variables.find((variable: Variable) => variable.key === 'link')?.value.split('\n').filter((url: string) => url && !url.includes("QH2-TGUlwu4")) || [];

      // Update the return value to match the new response format
      return {
        messages: [...messages, { role: 'assistant', content: data.value, pictures: pictureUrls }],
        response: data.value,
      };
    } catch (error) {
      console.error(`Error while sending message: ${(error as Error).message}`);
      return {
        messages: [],
        response: 'Error while sending message',
      };
    }
  }
}
