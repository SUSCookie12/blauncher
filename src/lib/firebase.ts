import { initializeApp } from "firebase/app";
import { getFirestore } from "firebase/firestore";

const firebaseConfig = {
  apiKey: "AIzaSyAxkQA5tBqp_5fco6Y_8i2mhI15ECJeNh0",
  authDomain: "cookiemovie-27669.firebaseapp.com",
  databaseURL: "https://cookiemovie-27669-default-rtdb.firebaseio.com",
  projectId: "cookiemovie-27669",
  storageBucket: "cookiemovie-27669.firebasestorage.app",
  messagingSenderId: "845488313573",
  appId: "1:845488313573:web:e4928462b97eb4dda4300c",
  measurementId: "G-YSQ6FLGCH1"
};

const app = initializeApp(firebaseConfig);
export const db = getFirestore(app);
