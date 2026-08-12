import React from"react";import ReactDOM from"react-dom/client";import{HashRouter}from"react-router-dom";import{AuthProvider}from"./auth";import App from"./App";import"./styles.css";import"./v160-final.css";
ReactDOM.createRoot(document.getElementById("root")!).render(<React.StrictMode><HashRouter><AuthProvider><App/></AuthProvider></HashRouter></React.StrictMode>);
