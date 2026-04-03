import { doc, getDoc } from "firebase/firestore";
import { db } from "./firebase";

export interface LauncherMetadata {
  version: string;
  downloadUrl: string;
}

export async function getLauncherMetadata(): Promise<LauncherMetadata | null> {
  const docRef = doc(db, "services", "blauncher");
  const docSnap = await getDoc(docRef);

  if (docSnap.exists()) {
    return docSnap.data() as LauncherMetadata;
  } else {
    console.error("Launcher metadata document 'services/blauncher' doesn't exist!");
    return null;
  }
}
