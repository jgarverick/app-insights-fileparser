# Azure Application Insights Flat File Parser for Kubernetes

This component is meant to be implemented as a container or sidecar on a Kubernetes pod or deployment where:

- the pod is configured to write to a flat file for logging and 
- where the code for the app writing to flat files does not support Application Insights integration directly (i.e. C++)

## How it works

The component `BackgroundLogWatcher` which inherits from abstract class `Microsoft.Extensions.Hosting.BackgroundService` watches the file for changes and parses the file for Application Insights trace records or exceptions should they be written to file. Unhandled exceptions within the API will also be captured as Application Insights is configured at the service level. When invoking the `StartAsync` method, the component will start a background thread to watch the file(s) for changes.

## Building and Running the Docker Image
The Dockerfile in this solution can be used with Windows or Linux. You will need to specify the Docker engine to use when building in a pipeline. Two environment variables (`LOG_PATH` and `APPINSIGHTS_INSTRUMENTATIONKEY`) are required to be set.

For local testing, a `docker run` command can be used to run the container:
```powershell
docker run -it --rm -p 8088:80 -e LOG_PATH=/var/log/test -e APPINSIGHTS_INSTRUMENTATIONKEY=YOURUNIQUEKEY -v /local/path/to/testlogs:/var/log/test <NAME OF DOCKER IMAGE>
```

For running in a Kubernetes cluster, the following `container` spec can be used:
```yaml
containers:
# App code container spec
  - name: your-app-code
    image: <YOUR APP CODE DOCKER IMAGE URL>
    imagePullPolicy: IfNotPresent
    volumeMounts:
      - name: testlogs
        mountPath: /var/log/test
# File parser spec
  - name: appinsights-file-parser
    image: <CONTAINER-REGISTRY>/<NAME OF DOCKER IMAGE>
    imagePullPolicy: IfNotPresent
    ports:
      - 80
      - 443
    env:
      - name: LOG_PATH
        value: /var/log/test
      - name: APPINSIGHTS_INSTRUMENTATIONKEY
        value: YOURUNIQUEKEY
    livenessProbe:
    httpGet:
      path: /health
      port: 80
      timeoutSeconds: 5
      periodSeconds: 10
      successThreshold: 1
      failureThreshold: 6
    readinessProbe:
    httpGet:
      path: /ready
      port: 80
      timeoutSeconds: 5
      periodSeconds: 10
      successThreshold: 1
      failureThreshold: 6
    volumeMounts:
      - name: testlogs
        mountPath: /var/log/test
volumes:
    - name: testlogs
      persistentVolumeClaim:
        claimName: testlogs-shared-pvc

```

## API Construction
This ASP.NET Core Web API has three routes that can be accessed:

- `/`: Initiates the BackgroundService and starts the watcher process on the path provided in `LOG_PATH`
- `/health`: Returns a 200 OK response if the API is running
- `/ready`: Returns a 200 OK response if the watcher process is running along with a count of files in the directory, and a readout of the files being monitored along with their last line processed. It's important to note that if the main API path has not been activated, this route will attempt to initiate the BackgroundService and start the watcher process.