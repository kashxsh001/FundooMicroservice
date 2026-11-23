pipeline {
    agent any

    options {
        timestamps()
        disableConcurrentBuilds()
    }

    triggers {
        pollSCM('H/5 * * * *')
    }

    environment {
        DOCKER_HUB_USER = 'kashxsh001'
        IMAGE_TAG = "build-${BUILD_NUMBER}"
        KUBECONFIG = '/tmp/kubeconfig'
    }

    stages {
        stage('Build and push images') {
            steps {
                withCredentials([usernamePassword(
                    credentialsId: 'docker-hub-creds',
                    usernameVariable: 'DOCKER_USER',
                    passwordVariable: 'DOCKER_PASSWORD'
                )]) {
                    sh '''
                        set -eu
                        echo "$DOCKER_PASSWORD" | docker login --username "$DOCKER_USER" --password-stdin

                        build_and_push() {
                            image_name="$1"
                            project_path="$2"
                            image="$DOCKER_HUB_USER/$image_name"

                            docker build \
                                --file Dockerfile \
                                --build-arg "PROJECT_PATH=$project_path" \
                                --tag "$image:$IMAGE_TAG" \
                                --tag "$image:live" \
                                .
                            docker push "$image:$IMAGE_TAG"
                            docker push "$image:live"
                        }

                        build_and_push fundoo-user src/Services/User/Fundoo.User.Api/Fundoo.User.Api.csproj
                        build_and_push fundoo-notes src/Services/Notes/Fundoo.Notes.Api/Fundoo.Notes.Api.csproj
                        build_and_push fundoo-notification src/Services/Notification/Fundoo.Notification.Api/Fundoo.Notification.Api.csproj
                        build_and_push fundoo-gateway src/ApiGateway/Fundoo.ApiGateway/Fundoo.ApiGateway.csproj

                        docker logout
                    '''
                }
            }
        }

        stage('Deploy to K3s') {
            steps {
                sh '''
                    set -eu
                    kubectl --namespace fundoo set image deployment/user-api user-api="$DOCKER_HUB_USER/fundoo-user:$IMAGE_TAG"
                    kubectl --namespace fundoo set image deployment/notes-api notes-api="$DOCKER_HUB_USER/fundoo-notes:$IMAGE_TAG"
                    kubectl --namespace fundoo set image deployment/notification-api notification-api="$DOCKER_HUB_USER/fundoo-notification:$IMAGE_TAG"
                    kubectl --namespace fundoo set image deployment/gateway gateway="$DOCKER_HUB_USER/fundoo-gateway:$IMAGE_TAG"

                    kubectl --namespace fundoo rollout status deployment/user-api --timeout=240s
                    kubectl --namespace fundoo rollout status deployment/notes-api --timeout=240s
                    kubectl --namespace fundoo rollout status deployment/notification-api --timeout=240s
                    kubectl --namespace fundoo rollout status deployment/gateway --timeout=240s
                '''
            }
        }
    }

    post {
        always {
            sh 'docker logout >/dev/null 2>&1 || true'
        }
    }
}
